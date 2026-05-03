using System.Globalization;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Domain.Enums;
using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Infrastructure.Persistence;
using MSEMC.Messaging.Commands;

namespace Platform.AiNewsDelivery.Infrastructure.Dispatchers;

public interface IDigestDispatcher
{
    Task DispatchPendingChangesAsync(CancellationToken cancellationToken = default);
}

public sealed partial class DigestDispatcher : IDigestDispatcher
{
    private readonly NewsDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly NewsWorkerOptions _workerOptions;
    private readonly ILogger<DigestDispatcher> _logger;

    /// <summary>Mapeamento de provider interno → label humano para o email.</summary>
    private static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openrouter"] = "OpenRouter",
        ["huggingface"] = "HuggingFace",
        ["ArtificialAnalysis"] = "Artificial Analysis"
    };

    public DigestDispatcher(
        NewsDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IOptions<NewsWorkerOptions> workerOptions,
        ILogger<DigestDispatcher> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _workerOptions = workerOptions.Value;
        _logger = logger;
    }

    public async Task DispatchPendingChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingChanges = await _dbContext.ModelChanges
                .Where(c => !c.Dispatched && c.Severity != ChangeSeverity.Silent)
                .OrderBy(c => c.DetectedAt)
                .ToListAsync(cancellationToken);

            if (pendingChanges.Count == 0)
            {
                LogNoPendingChanges();
                return;
            }

            var dataPayload = await BuildEnrichedPayloadAsync(pendingChanges, cancellationToken);

            var command = new SendLlmDigestCommand(
                MessageId: Guid.NewGuid(),
                Recipient: _workerOptions.AdminEmail,
                TemplateId: "ai-news/ai-news-digest",
                Data: dataPayload,
                Locale: "pt-BR",
                CreatedAt: DateTimeOffset.UtcNow
            );

            var now = DateTime.UtcNow;
            foreach (var change in pendingChanges)
            {
                change.Dispatched = true;
                change.DispatchedAt = now;
            }

            // Publica no mensageiro (MassTransit injeta a mensagem no Outbox via EF Core context)
            await _publishEndpoint.Publish(command, cancellationToken);
            LogCommandPublished(command.MessageId, pendingChanges.Count);

            // Salva as mudanças e as mensagens no Outbox atomicamente
            await _dbContext.SaveChangesAsync(cancellationToken);
            LogChangesMarkedAsDispatched(pendingChanges.Count);
        }
        catch (Exception ex)
        {
            LogDispatchFailed(ex);
            throw;
        }
    }

    // ── Payload Enrichment Pipeline ─────────────────────────────────────────────

    private async Task<object> BuildEnrichedPayloadAsync(
        List<Domain.Entities.ModelChange> changes, 
        CancellationToken cancellationToken)
    {
        // 1. Buscar métricas atuais com query otimizada (evita N+1)
        var modelIds = changes.Select(c => c.ModelId).Distinct().ToList();
        var latestSnapshots = await FetchLatestSnapshotsAsync(modelIds, cancellationToken);

        // 2. Converter changes brutas para DTOs enriquecidos
        var changeDtos = changes.Select(c =>
        {
            var snapshot = latestSnapshots.GetValueOrDefault(c.ModelId);
            return new LlmChangeDto(
                ModelId: c.ModelId,
                Provider: c.Provider,
                ChangeType: c.ChangeType.ToString(),
                Severity: c.Severity.ToString(),
                FieldName: c.FieldName,
                OldValue: c.OldValue,
                NewValue: c.NewValue,
                Description: BuildDescription(c),
                PricePerMillion: snapshot?.Metrics?.PricePerMillion?.ToString("0.######", CultureInfo.InvariantCulture),
                ContextWindow: snapshot?.Metrics?.ContextWindow?.ToString(CultureInfo.InvariantCulture),
                SourceUrl: snapshot?.Metadata?.SourceUrl
            );
        }).ToList();

        // 3. Agrupar por Provider → ModelId (hierarquia de 2 níveis)
        var providerGroups = BuildProviderGroups(changeDtos, latestSnapshots);

        // 4. Calcular sumário executivo
        var summary = BuildSummary(changeDtos, latestSnapshots);

        LogPayloadBuilt(providerGroups.Count, summary.TotalChanges, summary.TotalNewModels);

        return new
        {
            ReferenceDate = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture),
            Summary = summary,
            ProviderGroups = providerGroups,
            // Backward compatibility: mantém a lista plana para consumidores antigos
            Changes = changeDtos
        };
    }

    /// <summary>
    /// Busca o snapshot mais recente de cada modelo em uma única query (evita N+1).
    /// Retorna dicionário ModelId → (Metrics, Metadata) desserializados.
    /// </summary>
    private async Task<Dictionary<string, SnapshotEnrichment>> FetchLatestSnapshotsAsync(
        List<string> modelIds,
        CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.ModelSnapshots
            .Where(s => modelIds.Contains(s.ModelId))
            .GroupBy(s => s.ModelId)
            .Select(g => g.OrderByDescending(s => s.CollectedAt).First())
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, SnapshotEnrichment>(StringComparer.OrdinalIgnoreCase);

        foreach (var snap in snapshots)
        {
            var metrics = TryDeserialize<Metrics>(snap.MetricsJson);
            var metadata = TryDeserialize<ModelMetadata>(snap.MetadataJson);
            result[snap.ModelId] = new SnapshotEnrichment(metrics, metadata);
        }

        return result;
    }

    private static List<ProviderGroupDto> BuildProviderGroups(
        List<LlmChangeDto> allChanges,
        Dictionary<string, SnapshotEnrichment> snapshots)
    {
        return allChanges
            .GroupBy(c => c.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(providerGroup =>
            {
                var provider = providerGroup.Key;
                var models = providerGroup
                    .GroupBy(c => c.ModelId, StringComparer.OrdinalIgnoreCase)
                    .Select(modelGroup =>
                    {
                        var modelChanges = modelGroup.ToList();
                        var snapshot = snapshots.GetValueOrDefault(modelGroup.Key);
                        var badges = ComputeBadges(modelChanges);
                        var highestSeverity = modelChanges.Any(c => c.Severity == "Breaking") ? "Breaking" : "Digest";

                        return new ModelDigestDto(
                            ModelId: modelGroup.Key,
                            HighestSeverity: highestSeverity,
                            Changes: modelChanges,
                            PricePerMillion: snapshot?.Metrics?.PricePerMillion?.ToString("0.######", CultureInfo.InvariantCulture),
                            ContextWindow: FormatContextWindow(snapshot?.Metrics?.ContextWindow),
                            SourceUrl: snapshot?.Metadata?.SourceUrl,
                            TechnicalPaperUrl: snapshot?.Metadata?.TechnicalPaperUrl,
                            Badges: badges
                        );
                    })
                    .OrderByDescending(m => m.HighestSeverity == "Breaking")
                    .ThenBy(m => m.ModelId)
                    .ToList();

                return new ProviderGroupDto(
                    Provider: provider,
                    ProviderLabel: ProviderLabels.GetValueOrDefault(provider, provider),
                    ModelCount: models.Count,
                    Models: models
                );
            })
            .OrderByDescending(g => g.Models.Any(m => m.HighestSeverity == "Breaking"))
            .ThenByDescending(g => g.ModelCount)
            .ToList();
    }

    private static DigestSummaryDto BuildSummary(
        List<LlmChangeDto> allChanges,
        Dictionary<string, SnapshotEnrichment> snapshots)
    {
        var totalNewModels = allChanges.Count(c => c.ChangeType == "NewModel");
        var providersAffected = allChanges
            .Select(c => ProviderLabels.GetValueOrDefault(c.Provider, c.Provider))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        // Fórmula de eficiência: Score = ContextWindow / PricePerMillion
        BestCostBenefitDto? bestCostBenefit = null;
        var candidates = allChanges
            .Where(c => c.ChangeType == "NewModel")
            .Select(c => c.ModelId)
            .Distinct()
            .Select(modelId =>
            {
                var snap = snapshots.GetValueOrDefault(modelId);
                if (snap?.Metrics?.PricePerMillion is not > 0 || snap.Metrics.ContextWindow is not > 0)
                    return (ModelId: modelId, Score: 0.0, Price: (decimal?)null, Context: (int?)null);

                var score = (double)snap.Metrics.ContextWindow.Value / (double)snap.Metrics.PricePerMillion.Value;
                return (ModelId: modelId, Score: score, Price: snap.Metrics.PricePerMillion, Context: snap.Metrics.ContextWindow);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (candidates.Score > 0)
        {
            bestCostBenefit = new BestCostBenefitDto(
                ModelId: candidates.ModelId,
                PricePerMillion: candidates.Price!.Value.ToString("0.######", CultureInfo.InvariantCulture),
                ContextWindow: FormatContextWindow(candidates.Context),
                EfficiencyScore: Math.Round(candidates.Score, 0)
            );
        }

        return new DigestSummaryDto(
            TotalNewModels: totalNewModels,
            TotalChanges: allChanges.Count,
            ProvidersAffected: providersAffected,
            BestCostBenefit: bestCostBenefit
        );
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static List<string> ComputeBadges(List<LlmChangeDto> changes)
    {
        var badges = new List<string>();
        if (changes.Any(c => c.ChangeType == "NewModel")) badges.Add("new");
        if (changes.Any(c => c.ChangeType == "PriceChange")) badges.Add("price-drop");
        if (changes.Any(c => c.ChangeType == "ContextWindowChange")) badges.Add("context-upgrade");
        if (changes.Any(c => c.ChangeType == "TestingOpportunity")) badges.Add("testing");
        if (changes.Any(c => c.ChangeType == "ThroughputChange")) badges.Add("speed-boost");
        if (changes.Any(c => c.ChangeType == "EloChange")) badges.Add("elo-rise");
        return badges;
    }

    /// <summary>Formata context window para exibição humana: 128000 → "128k", 1000000 → "1M".</summary>
    private static string? FormatContextWindow(int? contextWindow)
    {
        if (contextWindow is null) return null;
        return contextWindow.Value switch
        {
            >= 1_000_000 => $"{contextWindow.Value / 1_000_000.0:0.#}M",
            >= 1_000 => $"{contextWindow.Value / 1_000.0:0.#}k",
            _ => contextWindow.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }

    private static string BuildDescription(Domain.Entities.ModelChange change)
    {
        return change.ChangeType switch
        {
            ChangeType.NewModel => $"Novo modelo detectado: {change.ModelId}.",
            ChangeType.PriceChange => $"Preço de {change.FieldName} alterado em {Math.Round(change.DeltaPercent ?? 0, 2)}% (de ${change.OldValue} para ${change.NewValue}).",
            ChangeType.ContextWindowChange => $"Janela de contexto aumentada em {Math.Round(change.DeltaPercent ?? 0, 2)}% (de {change.OldValue} para {change.NewValue} tokens).",
            ChangeType.ThroughputChange => $"Velocidade de processamento aumentada em {Math.Round(change.DeltaPercent ?? 0, 2)}% (de {change.OldValue} para {change.NewValue} tokens/s).",
            ChangeType.TtftChange => $"Latência (TTFT) reduzida em {Math.Round(change.DeltaPercent ?? 0, 2)}% (agora responde em {change.NewValue} ms).",
            ChangeType.EloChange => $"Pontuação no ranking Elo aumentou {Math.Round(change.DeltaPercent ?? 0, 2)}% (de {change.OldValue} para {change.NewValue}).",
            ChangeType.TestingOpportunity => $"Oportunidade de Teste: Modelo {change.ModelId} entrou no OpenRouter, mas ainda não possui métricas na Artificial Analysis.",
            _ => $"Mudança detectada no modelo {change.ModelId} ({change.ChangeType})."
        };
    }

    /// <summary>Cache interno para métricas e metadados enriquecidos de um snapshot.</summary>
    private sealed record SnapshotEnrichment(Metrics? Metrics, ModelMetadata? Metadata);

    // ── LoggerMessage source generators ──────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: Não há mudanças pendentes para envio.")]
    private partial void LogNoPendingChanges();

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: Publicado SendLlmDigestCommand (Id: {CorrelationId}) com {Count} mudanças.")]
    private partial void LogCommandPublished(Guid correlationId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: {Count} mudanças marcadas como Dispatched=true.")]
    private partial void LogChangesMarkedAsDispatched(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: Payload enriquecido — {ProviderCount} provedores, {ChangeCount} mudanças, {NewModelCount} novos modelos.")]
    private partial void LogPayloadBuilt(int providerCount, int changeCount, int newModelCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "DigestDispatcher: Falha ao enviar digest.")]
    private partial void LogDispatchFailed(Exception ex);
}
