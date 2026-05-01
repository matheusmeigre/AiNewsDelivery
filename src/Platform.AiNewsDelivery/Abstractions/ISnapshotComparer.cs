using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Domain.Results;

namespace Platform.AiNewsDelivery.Abstractions;

/// <summary>Contrato para o motor de comparação de snapshots.</summary>
public interface ISnapshotComparer
{
    Task<Result<IReadOnlyList<ModelChange>>> CompareAsync(
        IReadOnlyList<CanonicalSnapshot> currentSnapshots,
        CancellationToken cancellationToken = default);
}
