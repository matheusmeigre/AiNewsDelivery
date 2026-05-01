using System.Globalization;
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
/// Coletor de dados do OpenRouter (<c>/api/v1/models</c>).
/// Extrai modelos com modalidade text, converte pricing para decimal/milhão,
/// e gera DataHash para detecção de mudanças O(1).
/// </summary>
internal sealed partial class OpenRouterExtractor : IExtractor
{
    public string SourceName => "openrouter";

    private readonly HttpClient _httpClient;
    private readonly SourceOptions _sourceOptions;
    private readonly ILogger<OpenRouterExtractor> _logger;

    // Usa JsonConstants.DefaultOptions no lugar do private field

    public OpenRouterExtractor(
        IHttpClientFactory httpClientFactory,
        IOptions<SourceOptions> sourceOptions,
        ILogger<OpenRouterExtractor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenRouter");
        _sourceOptions = sourceOptions.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<CanonicalSnapshot>>> ExtractAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_sourceOptions.OpenRouter.Enabled)
        {
            LogSourceDisabled();
            return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(Array.Empty<CanonicalSnapshot>());
        }

        try
        {
            LogExtractionStarted(_sourceOptions.OpenRouter.BaseUrl);

            var response = await _httpClient.GetAsync("models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<OpenRouterModelsResponse>(json, JsonConstants.DefaultOptions);

            if (modelsResponse?.Data is null || modelsResponse.Data.Count == 0)
            {
                LogEmptyResponse();
                return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(Array.Empty<CanonicalSnapshot>());
            }

            var collectedAt = DateTime.UtcNow;
            var snapshots = modelsResponse.Data
                .Where(IsTextModel)
                .Select(model => MapToCanonical(model, json, collectedAt))
                .ToList();

            LogExtractionCompleted(snapshots.Count, modelsResponse.Data.Count);
            return Result<IReadOnlyList<CanonicalSnapshot>>.Ok(snapshots);
        }
        catch (HttpRequestException ex)
        {
            LogHttpError(ex, ex.StatusCode?.ToString() ?? "unknown");
            return Result<IReadOnlyList<CanonicalSnapshot>>.Fail(
                $"OpenRouter HTTP error: {ex.StatusCode} — {ex.Message}");
        }
        catch (JsonException ex)
        {
            LogDeserializationError(ex);
            return Result<IReadOnlyList<CanonicalSnapshot>>.Fail(
                $"OpenRouter deserialization error: {ex.Message}");
        }
    }

    /// <summary>Filtra modelos cuja modalidade inclua "text" como output.</summary>
    private static bool IsTextModel(OpenRouterModel model)
    {
        var modality = model.Architecture?.Modality;
        if (string.IsNullOrWhiteSpace(modality))
            return true; // sem modalidade declarada — incluir por precaução

        return modality.Contains("text", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converte um <see cref="OpenRouterModel"/> para o formato canônico.
    /// Pricing: <c>string (USD/token) × 1.000.000 → decimal (USD/milhão)</c>.
    /// </summary>
    private static CanonicalSnapshot MapToCanonical(
        OpenRouterModel model, string fullJson, DateTime collectedAt)
    {
        var modelJson = JsonSerializer.Serialize(model, JsonConstants.DefaultOptions);

        return new CanonicalSnapshot(
            ModelId: model.Id,
            Provider: "openrouter",
            Metrics: new Metrics
            {
                PricePerMillion = ParsePricePerMillion(model.Pricing?.Prompt),
                ContextWindow = model.ContextLength ?? model.TopProvider?.ContextLength,
                EloRating = null,       // OpenRouter não expõe ELO
                TokensPerSec = null     // Precisa de Artificial Analysis (Sprint 5)
            },
            Metadata: new ModelMetadata
            {
                IsNew = false,          // Será determinado na Sprint 3 (comparação)
                SourceUrl = $"https://openrouter.ai/{model.Id}",
                ReleaseDate = model.Created.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(model.Created.Value).UtcDateTime
                    : null,
                TechnicalPaperUrl = null
            },
            CollectedAt: collectedAt
        )
        {
            RawData = modelJson,
            DataHash = DataHasher.ComputeHash(modelJson)
        };
    }

    /// <summary>
    /// Converte o preço por token (string) para preço por milhão (decimal).
    /// Ex: "0.000003" → 3.00m
    /// </summary>
    internal static decimal? ParsePricePerMillion(string? pricePerToken)
    {
        if (string.IsNullOrWhiteSpace(pricePerToken))
            return null;

        if (!decimal.TryParse(pricePerToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken))
            return null;

        if (perToken == 0m)
            return 0m;

        return perToken * 1_000_000m;
    }

    // ── LoggerMessage source generators ──────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenRouter: extração iniciada. BaseUrl={BaseUrl}")]
    private partial void LogExtractionStarted(string baseUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenRouter: {TextModels} modelos text extraídos (de {TotalModels} total).")]
    private partial void LogExtractionCompleted(int textModels, int totalModels);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenRouter: resposta vazia — nenhum modelo retornado.")]
    private partial void LogEmptyResponse();

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenRouter: fonte desabilitada na configuração. Pulando coleta.")]
    private partial void LogSourceDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "OpenRouter: erro HTTP. StatusCode={StatusCode}")]
    private partial void LogHttpError(Exception ex, string statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "OpenRouter: falha ao desserializar resposta JSON.")]
    private partial void LogDeserializationError(Exception ex);
}
