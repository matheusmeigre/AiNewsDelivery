using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Abstractions;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Domain.Results;
using Platform.AiNewsDelivery.Infrastructure.Extractors.DTOs;

namespace Platform.AiNewsDelivery.Infrastructure.Extractors;

/// <summary>
/// Coletor de dados do HuggingFace Hub (<c>/api/models</c>).
/// Abordagem híbrida:
/// <list type="number">
/// <item>Sweep primário: top-100 por downloads (modelos mais utilizados).</item>
/// <item>Sweep secundário: top-20 por last modified (atualizações recentes).</item>
/// </list>
/// Os dois sweeps são mesclados, eliminando duplicatas pelo <c>ModelId</c>.
/// </summary>
internal sealed partial class HuggingFaceExtractor : IExtractor
{
    public string SourceName => "huggingface";

    private readonly HttpClient _httpClient;
    private readonly SourceOptions _sourceOptions;
    private readonly ThresholdOptions _thresholds;
    private readonly ILogger<HuggingFaceExtractor> _logger;

    // Usa JsonConstants.DefaultOptions no lugar do private field

    /// <summary>Período para considerar um modelo como "novo" (7 dias).</summary>
    private static readonly TimeSpan NewModelThreshold = TimeSpan.FromDays(7);

    public HuggingFaceExtractor(
        IHttpClientFactory httpClientFactory,
        IOptions<SourceOptions> sourceOptions,
        IOptions<NewsWorkerOptions> workerOptions,
        ILogger<HuggingFaceExtractor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("HuggingFace");
        _sourceOptions = sourceOptions.Value;
        _thresholds = workerOptions.Value.Thresholds;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<CanonicalSnapshot>>> ExtractAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_sourceOptions.HuggingFace.Enabled)
        {
            LogSourceDisabled();
            return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(Array.Empty<CanonicalSnapshot>());
        }

        try
        {
            LogExtractionStarted(_sourceOptions.HuggingFace.BaseUrl);

            // ── Sweep 1: Top-100 por downloads (modelos mais usados) ────────
            var downloadModels = await FetchModelsAsync(
                "models?pipeline_tag=text-generation&sort=downloads&direction=-1&limit=100",
                "downloads",
                cancellationToken);

            // ── Sweep 2: Top-20 por lastModified (atualizações recentes) ────
            var recentModels = await FetchModelsAsync(
                "models?pipeline_tag=text-generation&sort=lastModified&direction=-1&limit=20",
                "lastModified",
                cancellationToken);

            // ── Merge com deduplicação por ModelId + Filtro Precoce ─────────
            var collectedAt = DateTime.UtcNow;
            var mergedModels = downloadModels
                .Concat(recentModels)
                .Where(m => m.Downloads >= _thresholds.HuggingFaceMinDownloads)
                .DistinctBy(m => m.ModelId)
                .ToList();

            var snapshots = mergedModels
                .Select(dto => MapToCanonical(dto, collectedAt))
                .ToList();

            LogExtractionCompleted(snapshots.Count, downloadModels.Count, recentModels.Count);
            return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(snapshots);
        }
        catch (HttpRequestException ex)
        {
            LogHttpError(ex, ex.StatusCode?.ToString() ?? "unknown");
            return Result<IReadOnlyList<CanonicalSnapshot>>.Fail(
                $"HuggingFace HTTP error: {ex.StatusCode} — {ex.Message}");
        }
        catch (JsonException ex)
        {
            LogDeserializationError(ex);
            return Result<IReadOnlyList<CanonicalSnapshot>>.Fail(
                $"HuggingFace deserialization error: {ex.Message}");
        }
    }

    /// <summary>
    /// Busca modelos de um endpoint específico do HuggingFace.
    /// Adiciona o header Authorization quando o token estiver configurado.
    /// </summary>
    private async Task<IReadOnlyList<HuggingFaceModelDto>> FetchModelsAsync(
        string relativeUrl, string sweepName, CancellationToken cancellationToken)
    {
        LogSweepStarted(sweepName, relativeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);

        // Token opcional — aumenta rate limit quando presente
        if (!string.IsNullOrWhiteSpace(_sourceOptions.HuggingFace.Token))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _sourceOptions.HuggingFace.Token);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var response = await _httpClient.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // A API retorna um array direto (sem wrapper)
        var models = JsonSerializer.Deserialize<IReadOnlyList<HuggingFaceModelDto>>(json, JsonConstants.DefaultOptions)
            ?? Array.Empty<HuggingFaceModelDto>();

        LogSweepCompleted(sweepName, models.Count);
        return models;
    }

    /// <summary>Mapeia um <see cref="HuggingFaceModelDto"/> para o formato canônico.</summary>
    private static CanonicalSnapshot MapToCanonical(HuggingFaceModelDto dto, DateTime collectedAt)
    {
        var modelJson = JsonSerializer.Serialize(dto, JsonConstants.DefaultOptions);

        return new CanonicalSnapshot(
            ModelId: dto.ModelId,
            Provider: "huggingface",
            Metrics: new Metrics
            {
                PricePerMillion = null,     // HuggingFace não tem pricing
                ContextWindow = null,       // Não disponível neste endpoint
                EloRating = null,
                TokensPerSec = null,
                Downloads = dto.Downloads
            },
            Metadata: new ModelMetadata
            {
                IsNew = dto.CreatedAt.HasValue
                    && (DateTime.UtcNow - dto.CreatedAt.Value) < NewModelThreshold,
                SourceUrl = $"https://huggingface.co/{dto.ModelId}",
                ReleaseDate = dto.CreatedAt,
                TechnicalPaperUrl = ExtractArxivUrl(dto.Tags)
            },
            CollectedAt: collectedAt
        )
        {
            RawData = modelJson,
            DataHash = DataHasher.ComputeHash(modelJson)
        };
    }

    /// <summary>
    /// Extrai a URL do paper do arXiv das tags do modelo, se disponível.
    /// Tags de exemplo: <c>"arxiv:2505.09388"</c>.
    /// </summary>
    internal static string? ExtractArxivUrl(IReadOnlyList<string>? tags)
    {
        if (tags is null) return null;

        var arxivTag = tags.FirstOrDefault(t =>
            t.StartsWith("arxiv:", StringComparison.OrdinalIgnoreCase));

        if (arxivTag is null) return null;

        var arxivId = arxivTag["arxiv:".Length..];
        return $"https://arxiv.org/abs/{arxivId}";
    }

    // ── LoggerMessage source generators ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "HuggingFace: extração iniciada. BaseUrl={BaseUrl}")]
    private partial void LogExtractionStarted(string baseUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "HuggingFace: {Total} modelos extraídos (downloads={DownloadCount}, recent={RecentCount}, após deduplicação).")]
    private partial void LogExtractionCompleted(int total, int downloadCount, int recentCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "HuggingFace: sweep '{SweepName}' iniciado. URL={Url}")]
    private partial void LogSweepStarted(string sweepName, string url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "HuggingFace: sweep '{SweepName}' completado. {Count} modelos retornados.")]
    private partial void LogSweepCompleted(string sweepName, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HuggingFace: fonte desabilitada na configuração. Pulando coleta.")]
    private partial void LogSourceDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "HuggingFace: erro HTTP. StatusCode={StatusCode}")]
    private partial void LogHttpError(Exception ex, string statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "HuggingFace: falha ao desserializar resposta JSON.")]
    private partial void LogDeserializationError(Exception ex);
}
