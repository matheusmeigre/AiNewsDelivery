namespace Platform.AiNewsDelivery.Domain.Enums;

/// <summary>Status do ciclo de vida de uma execução do Worker.</summary>
public enum WorkerRunStatus
{
    Running = 0,
    Success = 1,
    PartialFailure = 2,
    Failure = 3
}
