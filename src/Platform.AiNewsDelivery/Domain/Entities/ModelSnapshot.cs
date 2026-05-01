using Platform.AiNewsDelivery.Domain.Enums;

namespace Platform.AiNewsDelivery.Domain.Entities;

/// <summary>Snapshot bruto de um modelo coletado de uma fonte externa em um dado momento.</summary>
public sealed class ModelSnapshot
{
    public long Id { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; }
    public string DataHash { get; set; } = string.Empty;
    public string RawData { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
}
