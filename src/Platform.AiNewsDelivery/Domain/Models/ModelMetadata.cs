namespace Platform.AiNewsDelivery.Domain.Models;

/// <summary>Metadados qualitativos de um modelo de linguagem.</summary>
public sealed record ModelMetadata
{
    public bool IsNew { get; init; }
    public DateTime? ReleaseDate { get; init; }
    public string? TechnicalPaperUrl { get; init; }
    public string? SourceUrl { get; init; }
}
