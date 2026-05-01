namespace Platform.AiNewsDelivery.Domain.Models;

/// <summary>Métricas quantitativas de um modelo. Todos os campos são nullable.</summary>
public sealed record Metrics
{
    public double? EloRating { get; init; }
    public double? TokensPerSec { get; init; }
    public decimal? PricePerMillion { get; init; }
    public int? ContextWindow { get; init; }
    public int? Downloads { get; init; }
    public double? TtftMs { get; init; }
}
