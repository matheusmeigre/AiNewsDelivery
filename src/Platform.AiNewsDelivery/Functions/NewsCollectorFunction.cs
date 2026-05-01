using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.AiNewsDelivery.Abstractions;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Enums;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Functions;

/// <summary>
/// Orquestrador do ciclo de coleta. Executa todos os <see cref="IExtractor"/>
/// registrados no DI, persiste os snapshots no banco, e mantém o audit trail.
/// Sprint 2: Coleta + Persistência. Sprint 3: Comparação.
/// </summary>
public sealed partial class NewsCollectorFunction
{
    private readonly ILogger<NewsCollectorFunction> _logger;
    private readonly NewsDbContext _dbContext;
    private readonly IEnumerable<IExtractor> _extractors;
    private readonly ISnapshotComparer _snapshotComparer;
    private readonly Platform.AiNewsDelivery.Infrastructure.Dispatchers.IDigestDispatcher _dispatcher;

    public NewsCollectorFunction(
        ILogger<NewsCollectorFunction> logger,
        NewsDbContext dbContext,
        IEnumerable<IExtractor> extractors,
        ISnapshotComparer snapshotComparer,
        Platform.AiNewsDelivery.Infrastructure.Dispatchers.IDigestDispatcher dispatcher)
    {
        _logger = logger;
        _dbContext = dbContext;
        _extractors = extractors;
        _snapshotComparer = snapshotComparer;
        _dispatcher = dispatcher;
    }

    [Function(nameof(NewsCollectorFunction))]
    public async Task Run(
        [TimerTrigger("%Worker:CronSchedule%", UseMonitor = false
#if DEBUG
        , RunOnStartup = true
#endif
        )] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        var cycleStartedAt = DateTime.UtcNow;
        var nextOccurrence = timerInfo.ScheduleStatus?.Next;

        LogCycleHeader();
        LogCycleStarted(cycleStartedAt, nextOccurrence);

        // ── Audit trail: registra a execução ────────────────────────────────
        var workerRun = new WorkerRun { StartedAt = cycleStartedAt, Status = WorkerRunStatus.Running };
        _dbContext.WorkerRuns.Add(workerRun);
        await _dbContext.SaveChangesAsync(cancellationToken);
        LogWorkerRunRegistered(workerRun.Id, workerRun.Status);

        var totalModels = 0;
        var errors = new List<object>();
        var allSnapshots = new List<Domain.Models.CanonicalSnapshot>();

        try
        {
            // ── Executa cada extractor registrado ───────────────────────────
            foreach (var extractor in _extractors)
            {
                try 
                {
                    LogExtractorStarting(extractor.SourceName);
                    var swExtractor = System.Diagnostics.Stopwatch.StartNew();
                    
                    var result = await extractor.ExtractAsync(cancellationToken);
                    swExtractor.Stop();

                    if (result.IsSuccess)
                    {
                        var snapshots = result.Value!;
                        var persisted = await PersistSnapshots(snapshots, cancellationToken);
                        totalModels += persisted;
                        allSnapshots.AddRange(snapshots);
                        LogExtractorCompleted(extractor.SourceName, persisted, swExtractor.ElapsedMilliseconds);
                    }
                    else
                    {
                        errors.Add(new { source = extractor.SourceName, error = result.Error, timestamp = DateTime.UtcNow });
                        LogExtractorFailed(extractor.SourceName, result.Error ?? "Erro desconhecido");
                    }
                }
                catch (Exception ex)
                {
                    LogExtractorCriticalError(ex, extractor.SourceName);
                    errors.Add(new { source = extractor.SourceName, error = ex.Message, timestamp = DateTime.UtcNow });
                }
            }

            // ── Executa motor de comparação ─────────────────────────────────
            var changesResult = await _snapshotComparer.CompareAsync(allSnapshots, cancellationToken);
            if (changesResult.IsSuccess && changesResult.Value != null && changesResult.Value.Count > 0)
            {
                _dbContext.ModelChanges.AddRange(changesResult.Value);
                workerRun.ChangesDetected = changesResult.Value.Count;
                LogChangesDetected(changesResult.Value.Count);
            }
            else if (!changesResult.IsSuccess)
            {
                errors.Add(new { source = "comparer", error = changesResult.Error, timestamp = DateTime.UtcNow });
                LogComparisonFailed(changesResult.Error ?? "Unknown error");
            }

            // Garante a persistência das mudanças ANTES do dispatch
            await _dbContext.SaveChangesAsync(cancellationToken);

            // ── Atualiza o audit trail e dispara Digest ───────────────────────
            workerRun.FinishedAt = DateTime.UtcNow;
            workerRun.ModelsCollected = totalModels;
            
            // Se coletamos algo, o status é pelo menos PartialSuccess
            workerRun.Status = errors.Count == 0 
                ? WorkerRunStatus.Success 
                : (totalModels > 0 ? WorkerRunStatus.PartialFailure : WorkerRunStatus.Failure);

            if (errors.Count > 0)
            {
                workerRun.ErrorsLog = JsonSerializer.Serialize(errors);
            }

            // Dispara se houver mudanças, mesmo que algum extrator tenha falhado
            await _dispatcher.DispatchPendingChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogCycleFailed(ex, workerRun.Id, WorkerRunStatus.Failure);
            workerRun.FinishedAt = DateTime.UtcNow;
            workerRun.Status = WorkerRunStatus.Failure;
            workerRun.ErrorsLog = JsonSerializer.Serialize(new[]
            {
                new { source = "orchestrator", error = ex.Message, timestamp = DateTime.UtcNow }
            });
        }
        finally
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            LogCycleFinished(workerRun.FinishedAt, workerRun.Duration, workerRun.Status, totalModels);
        }
    }

    /// <summary>
    /// Persiste snapshots no banco, pulando modelos cujo hash não mudou
    /// desde a última coleta (detecção de mudanças O(1)).
    /// </summary>
    private async Task<int> PersistSnapshots(IReadOnlyList<Domain.Models.CanonicalSnapshot> snapshots, CancellationToken cancellationToken)
    {
        var persisted = 0;

        foreach (var snapshot in snapshots)
        {
            // Verifica se já existe um snapshot com o mesmo hash para este modelo/provider
            var existingHash = await _dbContext.ModelSnapshots
                .Where(s => s.ModelId == snapshot.ModelId && s.Provider == snapshot.Provider)
                .OrderByDescending(s => s.CollectedAt)
                .Select(s => s.DataHash)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingHash == snapshot.DataHash)
                continue; // Nenhuma mudança — pula persistência

            _dbContext.ModelSnapshots.Add(new ModelSnapshot
            {
                ModelId = snapshot.ModelId,
                Provider = snapshot.Provider,
                CollectedAt = snapshot.CollectedAt,
                DataHash = snapshot.DataHash,
                RawData = snapshot.RawData,
                MetricsJson = JsonSerializer.Serialize(snapshot.Metrics),
                MetadataJson = JsonSerializer.Serialize(snapshot.Metadata)
            });

            persisted++;
        }

        if (persisted > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return persisted;
    }

    // ── LoggerMessage source generators ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "═══════════════════════════════════════")]
    private partial void LogCycleHeader();

    [LoggerMessage(Level = LogLevel.Information, Message = "Ciclo de coleta iniciado em {StartedAt:O}. Próxima execução: {NextOccurrence:O}")]
    private partial void LogCycleStarted(DateTime startedAt, DateTime? nextOccurrence);

    [LoggerMessage(Level = LogLevel.Debug, Message = "WorkerRun {RunId} registrado com status {Status}")]
    private partial void LogWorkerRunRegistered(long runId, WorkerRunStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Iniciando extractor: {SourceName}")]
    private partial void LogExtractorStarting(string sourceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extractor {SourceName}: {Count} modelos persistidos em {ElapsedMs}ms.")]
    private partial void LogExtractorCompleted(string sourceName, int count, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Extractor {SourceName} falhou: {Error}")]
    private partial void LogExtractorFailed(string sourceName, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado no extractor {SourceName}")]
    private partial void LogExtractorCriticalError(Exception ex, string sourceName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha crítica no ciclo de coleta. WorkerRun {RunId} marcado como {Status}.")]
    private partial void LogCycleFailed(Exception ex, long runId, WorkerRunStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ciclo finalizado em {FinishedAt:O}. Duração: {Duration}. Status: {Status}. Total modelos: {TotalModels}.")]
    private partial void LogCycleFinished(DateTime? finishedAt, TimeSpan? duration, WorkerRunStatus status, int totalModels);

    [LoggerMessage(Level = LogLevel.Information, Message = "Comparação finalizada: {Count} mudanças detectadas.")]
    private partial void LogChangesDetected(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha no motor de comparação: {Error}")]
    private partial void LogComparisonFailed(string error);
}
