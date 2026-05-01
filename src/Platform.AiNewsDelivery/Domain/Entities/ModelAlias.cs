namespace Platform.AiNewsDelivery.Domain.Entities;

public sealed class ModelAlias
{
    public int Id { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string CanonicalId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
