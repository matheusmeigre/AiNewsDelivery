using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Results;

namespace Platform.AiNewsDelivery.Abstractions;

/// <summary>Contrato para o despachante de alertas ao MSEMC.</summary>
public interface IDigestDispatcher
{
    Task<Result<int>> DispatchAsync(
        IReadOnlyList<ModelChange> changes,
        CancellationToken cancellationToken = default);
}
