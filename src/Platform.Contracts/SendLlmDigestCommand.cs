namespace MSEMC.Messaging.Commands;

public sealed record SendLlmDigestCommand(
    Guid MessageId,
    string Recipient,
    string TemplateId,
    object Data,
    string? Locale = "pt-BR",
    DateTimeOffset CreatedAt = default
);

// ── DTOs de Mudança Individual ──────────────────────────────────────────────────

public record LlmChangeDto(
    string ModelId, 
    string Provider, 
    string ChangeType,
    string Severity,
    string? FieldName, 
    string? OldValue, 
    string? NewValue,
    string Description,
    string? PricePerMillion = null,
    string? ContextWindow = null,
    string? SourceUrl = null
);

// ── DTOs de Sumário Executivo ───────────────────────────────────────────────────

/// <summary>Dashboard rápido do topo do email. Permite decisão instantânea sobre a relevância.</summary>
public record DigestSummaryDto(
    int TotalNewModels,
    int TotalChanges,
    List<string> ProvidersAffected,
    BestCostBenefitDto? BestCostBenefit
);

/// <summary>Modelo com melhor relação ContextWindow/PricePerMillion.</summary>
public record BestCostBenefitDto(
    string ModelId,
    string PricePerMillion,
    string? ContextWindow,
    double EfficiencyScore
);

// ── DTOs de Agrupamento Hierárquico ─────────────────────────────────────────────

/// <summary>Agrupamento de primeiro nível: todos os modelos de um provedor.</summary>
public record ProviderGroupDto(
    string Provider,
    string ProviderLabel,
    int ModelCount,
    List<ModelDigestDto> Models
);

/// <summary>
/// Card de modelo com todas as mudanças agrupadas, métricas inline e badges visuais.
/// Campos de métricas são nullable para resiliência (snapshot sem dados).
/// </summary>
public record ModelDigestDto(
    string ModelId,
    string HighestSeverity,
    List<LlmChangeDto> Changes,
    string? PricePerMillion,
    string? ContextWindow,
    string? SourceUrl,
    string? TechnicalPaperUrl,
    List<string> Badges
);
