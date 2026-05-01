using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Domain.Results;

namespace Platform.AiNewsDelivery.Abstractions;

/// <summary>Contrato base para coletores de dados de fontes externas.</summary>
public interface IExtractor
{
    string SourceName { get; }
    Task<Result<IReadOnlyList<CanonicalSnapshot>>> ExtractAsync(CancellationToken cancellationToken = default);
}
