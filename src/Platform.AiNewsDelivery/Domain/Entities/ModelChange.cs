using Platform.AiNewsDelivery.Domain.Enums;

namespace Platform.AiNewsDelivery.Domain.Entities;

/// <summary>Registro de uma mudança detectada pelo motor de comparação.</summary>
public sealed class ModelChange
{
    public long Id { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public ChangeType ChangeType { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public double? DeltaPercent { get; set; }
    public ChangeSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; }
    public bool Dispatched { get; set; }
    public DateTime? DispatchedAt { get; set; }
}
