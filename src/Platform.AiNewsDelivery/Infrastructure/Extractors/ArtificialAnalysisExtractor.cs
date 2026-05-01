using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Abstractions;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Domain.Results;
using Platform.AiNewsDelivery.Infrastructure;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Infrastructure.Extractors;

public sealed partial class ArtificialAnalysisExtractor : IExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ArtificialAnalysisExtractor> _logger;
    private readonly NewsDbContext _dbContext;
    private readonly string _fallbackFilePath = "artificial_analysis_fallback.json";

    // Usa JsonConstants.DefaultOptions no lugar do private field

    public ArtificialAnalysisExtractor(
        HttpClient httpClient,
        ILogger<ArtificialAnalysisExtractor> logger,
        NewsDbContext dbContext)
    {
        _httpClient = httpClient;
        _logger = logger;
        _dbContext = dbContext;
    }

    public string SourceName => "ArtificialAnalysis";

    public async Task<Result<IReadOnlyList<CanonicalSnapshot>>> ExtractAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rawModels = await FetchModelsAsync(cancellationToken);
            if (rawModels.Count == 0)
            {
                return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(Array.Empty<CanonicalSnapshot>());
            }

            var aliases = await _dbContext.ModelAliases.ToDictionaryAsync(a => a.ProviderId, a => a.CanonicalId, cancellationToken);
            var snapshots = new List<CanonicalSnapshot>();

            foreach (var raw in rawModels)
            {
                var providerId = raw.Id;
                // Usa o Alias se existir, caso contrário emite aviso
                if (!aliases.TryGetValue(providerId, out var canonicalId))
                {
                    LogMissingAlias(providerId);
                    canonicalId = providerId;
                }

                var metrics = new Metrics
                {
                    EloRating = raw.Metrics?.EloRating,
                    TokensPerSec = raw.Metrics?.TokensPerSec,
                    TtftMs = raw.Metrics?.TtftMs
                };

                var metadata = new ModelMetadata
                {
                    IsNew = false
                };

                var rawData = JsonSerializer.Serialize(raw, JsonConstants.DefaultOptions);

                var snapshot = new CanonicalSnapshot(
                    ModelId: canonicalId,
                    Provider: "ArtificialAnalysis",
                    Metrics: metrics,
                    Metadata: metadata,
                    CollectedAt: DateTime.UtcNow
                )
                {
                    RawData = rawData,
                    DataHash = DataHasher.ComputeHash(rawData)
                };

                snapshots.Add(snapshot);
            }

            return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(snapshots);
        }
        catch (Exception ex)
        {
            LogExtractionFailed(ex);
            return Result<IReadOnlyList<CanonicalSnapshot>>.Fail("Falha na extração.");
        }
    }

    private async Task<List<AAModelDto>> FetchModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.artificialanalysis.ai/v1/models", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<List<AAModelDto>>(content, JsonConstants.DefaultOptions);
                if (result != null) return result;
            }
        }
        catch (Exception ex)
        {
            LogApiFetchFailed(ex);
        }

        if (File.Exists(_fallbackFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_fallbackFilePath, cancellationToken);
                var result = JsonSerializer.Deserialize<List<AAModelDto>>(json, JsonConstants.DefaultOptions);
                if (result != null) return result;
            }
            catch (Exception ex)
            {
                LogFallbackFailed(ex, _fallbackFilePath);
            }
        }

        return new List<AAModelDto>();
    }

    // DTOs baseados na estrutura esperada de retorno
    private sealed class AAModelDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("metrics")]
        public AAMetricsDto? Metrics { get; set; }
    }

    private sealed class AAMetricsDto
    {
        [JsonPropertyName("elo_rating")]
        public double? EloRating { get; set; }

        [JsonPropertyName("tokens_per_sec")]
        public double? TokensPerSec { get; set; }

        [JsonPropertyName("ttft_ms")]
        public double? TtftMs { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao buscar Artificial Analysis via API. Tentando fallback local.")]
    private partial void LogApiFetchFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao ler o fallback local {FallbackPath}")]
    private partial void LogFallbackFailed(Exception ex, string fallbackPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao extrair dados do Artificial Analysis")]
    private partial void LogExtractionFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Governança: Nenhum alias encontrado para o modelo '{ProviderId}'. Usando ID original. Tradução manual recomendada.")]
    private partial void LogMissingAlias(string providerId);
}
