using Platform.AiNewsDelivery.Domain.Enums;

namespace Platform.AiNewsDelivery.Domain.Entities;

/// <summary>Registro de auditoria de cada execução do Worker.</summary>
public sealed class WorkerRun
{
    public long Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public WorkerRunStatus Status { get; set; }
    public int ModelsCollected { get; set; }
    public int ChangesDetected { get; set; }
    public string? ErrorsLog { get; set; }

    /// <summary>Duração computada em memória (não persistida pelo EF Core).</summary>
    public TimeSpan? Duration => FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;
}
