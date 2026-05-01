namespace MSEMC.Messaging.Commands;

public sealed record SendLlmDigestCommand(
    Guid MessageId,
    string Recipient,
    string TemplateId,
    object Data,
    string? Locale = "pt-BR",
    DateTimeOffset CreatedAt = default
);

public record LlmChangeDto(
    string ModelId, 
    string Provider, 
    string ChangeType, // NewModel, PriceChange, ContextWindowChange, etc.
    string Severity,   // Breaking ou Digest
    string? FieldName, 
    string? OldValue, 
    string? NewValue,
    string Description // Ex: "Preço reduzido em 25% (de $0.04 para $0.03)"
);
