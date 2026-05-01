namespace Platform.AiNewsDelivery.Domain.Models;

/// <summary>
/// Representação canônica de um modelo de linguagem em um dado momento.
/// Todas as fontes são normalizadas para este schema antes de persistência.
/// <para>
/// <c>RawData</c> e <c>DataHash</c> são propriedades de infraestrutura usadas
/// para serialização bruta e detecção de mudanças O(1), respectivamente.
/// </para>
/// </summary>
public sealed record CanonicalSnapshot(
    string ModelId,
    string Provider,
    Metrics Metrics,
    ModelMetadata Metadata,
    DateTime CollectedAt
)
{
    /// <summary>JSON bruto completo do modelo conforme retornado pela API de origem.</summary>
    public string RawData { get; init; } = string.Empty;

    /// <summary>SHA-256 (hex, 64 chars) do <see cref="RawData"/> para detecção de mudanças O(1).</summary>
    public string DataHash { get; init; } = string.Empty;
}
