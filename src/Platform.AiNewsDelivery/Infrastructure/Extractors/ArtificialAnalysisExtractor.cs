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
    private readonly SourceOptions _sourceOptions;
    private readonly ILogger<ArtificialAnalysisExtractor> _logger;
    private readonly NewsDbContext _dbContext;
    private readonly string _fallbackFilePath = "artificial_analysis_fallback.json";

    // Usa JsonConstants.DefaultOptions no lugar do private field

    public ArtificialAnalysisExtractor(
        HttpClient httpClient,
        IOptions<SourceOptions> sourceOptions,
        ILogger<ArtificialAnalysisExtractor> logger,
        NewsDbContext dbContext)
    {
        _httpClient = httpClient;
        _sourceOptions = sourceOptions.Value;
        _logger = logger;
        _dbContext = dbContext;
    }

    public string SourceName => "ArtificialAnalysis";

    public async Task<Result<IReadOnlyList<CanonicalSnapshot>>> ExtractAsync(CancellationToken cancellationToken = default)
    {
        if (!_sourceOptions.ArtificialAnalysis.Enabled)
        {
            LogSourceDisabled();
            return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(Array.Empty<CanonicalSnapshot>());
        }

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
                    EloRating = raw.Evaluations?.ArtificialAnalysisIntelligenceIndex,
                    TokensPerSec = raw.MedianOutputTokensPerSecond,
                    PricePerMillion = raw.Pricing?.Price1MBlended3To1,
                    TtftMs = raw.MedianTimeToFirstTokenSeconds is double ttftSeconds
                        ? ttftSeconds * 1000d
                        : null
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
            var response = await _httpClient.GetAsync(_sourceOptions.ArtificialAnalysis.ModelsPath.TrimStart('/'), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = DeserializeModels(content);
                if (result != null) return result;
            }

            LogApiReturnedUnexpectedStatus((int)response.StatusCode);
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
                var result = DeserializeModels(json);
                if (result != null) return result;
            }
            catch (Exception ex)
            {
                LogFallbackFailed(ex, _fallbackFilePath);
            }
        }

        return new List<AAModelDto>();
    }

    private static List<AAModelDto>? DeserializeModels(string json)
    {
        var response = JsonSerializer.Deserialize<AAApiResponseDto>(json, JsonConstants.DefaultOptions);
        if (response?.Data is { Count: > 0 })
        {
            return response.Data;
        }

        return JsonSerializer.Deserialize<List<AAModelDto>>(json, JsonConstants.DefaultOptions);
    }

    private sealed class AAApiResponseDto
    {
        [JsonPropertyName("data")]
        public List<AAModelDto>? Data { get; set; }
    }

    // DTOs baseados na estrutura esperada de retorno
    private sealed class AAModelDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("evaluations")]
        public AAEvaluationsDto? Evaluations { get; set; }

        [JsonPropertyName("pricing")]
        public AAPricingDto? Pricing { get; set; }

        [JsonPropertyName("median_output_tokens_per_second")]
        public double? MedianOutputTokensPerSecond { get; set; }

        [JsonPropertyName("median_time_to_first_token_seconds")]
        public double? MedianTimeToFirstTokenSeconds { get; set; }
    }

    private sealed class AAEvaluationsDto
    {
        [JsonPropertyName("artificial_analysis_intelligence_index")]
        public double? ArtificialAnalysisIntelligenceIndex { get; set; }
    }

    private sealed class AAPricingDto
    {
        [JsonPropertyName("price_1m_blended_3_to_1")]
        public decimal? Price1MBlended3To1 { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao buscar Artificial Analysis via API. Tentando fallback local.")]
    private partial void LogApiFetchFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Artificial Analysis desabilitado via configuração. Pulando coleta.")]
    private partial void LogSourceDisabled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Artificial Analysis retornou status HTTP inesperado {StatusCode}. Tentando fallback local.")]
    private partial void LogApiReturnedUnexpectedStatus(int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao ler o fallback local {FallbackPath}")]
    private partial void LogFallbackFailed(Exception ex, string fallbackPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao extrair dados do Artificial Analysis")]
    private partial void LogExtractionFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Governança: Nenhum alias encontrado para o modelo '{ProviderId}'. Usando ID original. Tradução manual recomendada.")]
    private partial void LogMissingAlias(string providerId);
}
