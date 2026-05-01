using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Abstractions;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Enums;
using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Domain.Results;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Infrastructure;

/// <summary>
/// Motor de comparação de snapshots. Identifica deltas matemáticos e filtra ruídos.
/// Implementa regras heurísticas de severidade baseadas em <see cref="ThresholdOptions"/>.
/// </summary>
internal sealed partial class SnapshotComparer : ISnapshotComparer
{
    private readonly NewsDbContext _dbContext;
    private readonly ThresholdOptions _thresholds;
    private readonly ILogger<SnapshotComparer> _logger;

    /// <summary>Watchlist para modelos novos. Se o ID contiver alguma destas strings, o NewModel será Breaking.</summary>
    private static readonly string[] _hardcodedWatchlist = ["openai/", "meta-llama/", "mistralai/", "anthropic/"];

    public SnapshotComparer(
        NewsDbContext dbContext,
        IOptions<NewsWorkerOptions> workerOptions,
        ILogger<SnapshotComparer> logger)
    {
        _dbContext = dbContext;
        _thresholds = workerOptions.Value.Thresholds;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ModelChange>>> CompareAsync(
        IReadOnlyList<CanonicalSnapshot> currentSnapshots,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var changes = new List<ModelChange>();
            var cycleStartedAt = DateTime.UtcNow; // Used as fallback

            foreach (var current in currentSnapshots)
            {
                // Busca o snapshot anterior (imediatamente anterior ao atual)
                var previousSnapshot = await _dbContext.ModelSnapshots
                    .Where(s => s.ModelId == current.ModelId && s.Provider == current.Provider && s.CollectedAt < current.CollectedAt)
                    .OrderByDescending(s => s.CollectedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (previousSnapshot is null)
                {
                    // É um modelo novo para este provedor
                    changes.Add(CreateNewModelChange(current));

                    // Watchlist de Diferencial Competitivo (Early Validation Opportunity)
                    if (string.Equals(current.Provider, "openrouter", StringComparison.OrdinalIgnoreCase))
                    {
                        var existsInAa = currentSnapshots.Any(s => string.Equals(s.Provider, "ArtificialAnalysis", StringComparison.OrdinalIgnoreCase) && s.ModelId == current.ModelId) ||
                                         await _dbContext.ModelSnapshots.AnyAsync(s => s.Provider == "ArtificialAnalysis" && s.ModelId == current.ModelId, cancellationToken);
                        
                        if (!existsInAa)
                        {
                            var isWatchlist = _hardcodedWatchlist.Any(w => current.ModelId.Contains(w, StringComparison.OrdinalIgnoreCase));
                            var severity = isWatchlist ? ChangeSeverity.Breaking : ChangeSeverity.Digest;

                            changes.Add(CreateChange(
                                current, 
                                ChangeType.TestingOpportunity, 
                                "Tier2Availability", 
                                "Unavailable", 
                                "Available in Tier 1", 
                                null, 
                                severity));
                        }
                    }

                    continue;
                }

                // Há histórico. Vamos comparar as métricas.
                var previousMetrics = string.IsNullOrWhiteSpace(previousSnapshot.MetricsJson)
                    ? new Metrics()
                    : JsonSerializer.Deserialize<Metrics>(previousSnapshot.MetricsJson) ?? new Metrics();

                CompareMetrics(current, previousMetrics, changes);
            }

            LogComparisonCompleted(changes.Count);
            return Result<IReadOnlyList<ModelChange>>.Ok(changes);
        }
        catch (Exception ex)
        {
            LogComparisonFailed(ex);
            return Result<IReadOnlyList<ModelChange>>.Fail($"Falha ao comparar snapshots: {ex.Message}");
        }
    }

    private static ModelChange CreateNewModelChange(CanonicalSnapshot current)
    {
        var isWatchlist = _hardcodedWatchlist.Any(w => current.ModelId.Contains(w, StringComparison.OrdinalIgnoreCase));
        
        return new ModelChange
        {
            ModelId = current.ModelId,
            Provider = current.Provider,
            ChangeType = ChangeType.NewModel,
            FieldName = null,
            OldValue = null,
            NewValue = null,
            DeltaPercent = null,
            Severity = isWatchlist ? ChangeSeverity.Breaking : ChangeSeverity.Digest,
            DetectedAt = DateTime.UtcNow,
            Dispatched = false
        };
    }

    private void CompareMetrics(CanonicalSnapshot current, Metrics previousMetrics, List<ModelChange> changes)
    {
        // 1. Queda de Preço
        if (current.Metrics.PricePerMillion.HasValue && previousMetrics.PricePerMillion.HasValue)
        {
            var newPrice = current.Metrics.PricePerMillion.Value;
            var oldPrice = previousMetrics.PricePerMillion.Value;

            if (newPrice < oldPrice && oldPrice > 0)
            {
                var dropPercent = (double)((oldPrice - newPrice) / oldPrice * 100);

                if (dropPercent >= _thresholds.PriceDropSignificantPercent)
                {
                    changes.Add(CreateChange(current, ChangeType.PriceChange, "PricePerMillion", oldPrice.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), newPrice.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), dropPercent, ChangeSeverity.Breaking));
                }
                else if (dropPercent >= _thresholds.PriceChangePercent)
                {
                    changes.Add(CreateChange(current, ChangeType.PriceChange, "PricePerMillion", oldPrice.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), newPrice.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), dropPercent, ChangeSeverity.Digest));
                }
                else
                {
                    changes.Add(CreateChange(current, ChangeType.PriceChange, "PricePerMillion", oldPrice.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), newPrice.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), dropPercent, ChangeSeverity.Silent));
                }
            }
        }

        // 2. Aumento de Contexto
        if (current.Metrics.ContextWindow.HasValue && previousMetrics.ContextWindow.HasValue)
        {
            var newContext = current.Metrics.ContextWindow.Value;
            var oldContext = previousMetrics.ContextWindow.Value;

            if (newContext > oldContext && oldContext > 0)
            {
                var increasePercent = (double)(newContext - oldContext) / oldContext * 100;
                // Qualquer aumento de contexto que consigamos detectar vamos considerar Digest, ou Breaking se for enorme.
                // Como não temos um threshold específico no WorkerOptions, vamos usar 10% fixo para Digest, ou Silent.
                var severity = increasePercent >= 10.0 ? ChangeSeverity.Digest : ChangeSeverity.Silent;
                changes.Add(CreateChange(current, ChangeType.ContextWindowChange, "ContextWindow", oldContext.ToString(System.Globalization.CultureInfo.InvariantCulture), newContext.ToString(System.Globalization.CultureInfo.InvariantCulture), increasePercent, severity));
            }
        }

        // 3. Aumento de Velocidade (Throughput)
        if (current.Metrics.TokensPerSec.HasValue && previousMetrics.TokensPerSec.HasValue)
        {
            var newTpS = current.Metrics.TokensPerSec.Value;
            var oldTpS = previousMetrics.TokensPerSec.Value;

            if (newTpS > oldTpS && oldTpS > 0)
            {
                var increasePercent = (newTpS - oldTpS) / oldTpS * 100;
                if (increasePercent >= _thresholds.TokensPerSecChangePercent)
                {
                    changes.Add(CreateChange(current, ChangeType.ThroughputChange, "TokensPerSec", oldTpS.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), newTpS.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), increasePercent, ChangeSeverity.Digest));
                }
            }
        }

        // 4. Redução de Latência (TTFT)
        if (current.Metrics.TtftMs.HasValue && previousMetrics.TtftMs.HasValue)
        {
            var newTtft = current.Metrics.TtftMs.Value;
            var oldTtft = previousMetrics.TtftMs.Value;

            if (newTtft < oldTtft && oldTtft > 0)
            {
                var decreasePercent = (oldTtft - newTtft) / oldTtft * 100;
                if (decreasePercent >= _thresholds.TtftImprovementPercent)
                {
                    changes.Add(CreateChange(current, ChangeType.TtftChange, "TtftMs", oldTtft.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), newTtft.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), decreasePercent, ChangeSeverity.Digest));
                }
            }
        }

        // 5. Mudança de ELO
        if (current.Metrics.EloRating.HasValue && previousMetrics.EloRating.HasValue)
        {
            var newElo = current.Metrics.EloRating.Value;
            var oldElo = previousMetrics.EloRating.Value;

            if (newElo > oldElo && oldElo > 0)
            {
                var increasePercent = (newElo - oldElo) / oldElo * 100;
                if (increasePercent >= _thresholds.EloChangePercent)
                {
                    changes.Add(CreateChange(current, ChangeType.EloChange, "EloRating", oldElo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), newElo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), increasePercent, ChangeSeverity.Digest));
                }
            }
        }
    }

    private static ModelChange CreateChange(CanonicalSnapshot current, ChangeType type, string field, string oldVal, string newVal, double? delta, ChangeSeverity severity)
    {
        return new ModelChange
        {
            ModelId = current.ModelId,
            Provider = current.Provider,
            ChangeType = type,
            FieldName = field,
            OldValue = oldVal,
            NewValue = newVal,
            DeltaPercent = delta,
            Severity = severity,
            DetectedAt = DateTime.UtcNow,
            Dispatched = false
        };
    }

    // ── LoggerMessage source generators ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "SnapshotComparer: comparação finalizada. {Count} mudanças detectadas.")]
    private partial void LogComparisonCompleted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "SnapshotComparer: falha crítica durante a comparação.")]
    private partial void LogComparisonFailed(Exception ex);
}
