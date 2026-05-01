using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.AiNewsDelivery.Domain.Enums;
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
    private readonly ILogger<DigestDispatcher> _logger;

    public DigestDispatcher(NewsDbContext dbContext, IPublishEndpoint publishEndpoint, ILogger<DigestDispatcher> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
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

            var changesDto = pendingChanges.Select(c => new LlmChangeDto(
                ModelId: c.ModelId,
                Provider: c.Provider, 
                ChangeType: c.ChangeType.ToString(),
                Severity: c.Severity.ToString(),
                FieldName: c.FieldName,
                OldValue: c.OldValue,
                NewValue: c.NewValue,
                Description: BuildDescription(c)
            )).ToList();

            var dataPayload = new
            {
                ReferenceDate = DateTime.UtcNow,
                Changes = changesDto
            };

            var command = new SendLlmDigestCommand(
                MessageId: Guid.NewGuid(),
                Recipient: "admin@platform.com", // TODO: Move to config
                TemplateId: "ai-news-digest",
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
            LogCommandPublished(command.MessageId, changesDto.Count);

            // Salva as mudanças e as mensagens no Outbox atomicamente
            await _dbContext.SaveChangesAsync(cancellationToken);
            LogChangesMarkedAsDispatched(pendingChanges.Count);
        }
        catch (Exception ex)
        {
            LogDispatchFailed(ex);
            throw; // Rethrow to let the orchestrator know it failed
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: Não há mudanças pendentes para envio.")]
    private partial void LogNoPendingChanges();

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: Publicado SendLlmDigestCommand (Id: {CorrelationId}) com {Count} mudanças.")]
    private partial void LogCommandPublished(Guid correlationId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "DigestDispatcher: {Count} mudanças marcadas como Dispatched=true.")]
    private partial void LogChangesMarkedAsDispatched(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "DigestDispatcher: Falha ao enviar digest.")]
    private partial void LogDispatchFailed(Exception ex);
}
