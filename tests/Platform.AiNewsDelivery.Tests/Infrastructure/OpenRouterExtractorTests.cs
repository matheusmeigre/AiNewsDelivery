using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Infrastructure.Extractors;
using Platform.AiNewsDelivery.Infrastructure.Extractors.DTOs;

namespace Platform.AiNewsDelivery.Tests.Infrastructure;

/// <summary>
/// Testes unitários do <see cref="OpenRouterExtractor"/>.
/// O HttpClient é mockado via <see cref="FakeHttpMessageHandler"/> para isolar do HTTP real.
/// </summary>
public sealed class OpenRouterExtractorTests
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SourceOptions _sourceOptions = new()
    {
        OpenRouter = new OpenRouterSourceOptions { BaseUrl = "https://openrouter.ai/api/v1", Enabled = true }
    };

    private OpenRouterExtractor CreateExtractor(HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var factory = new FakeHttpClientFactory(httpClient);

        return new OpenRouterExtractor(
            factory,
            Options.Create(_sourceOptions),
            NullLogger<OpenRouterExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_WithValidResponse_ShouldReturnCanonicalSnapshots()
    {
        // Arrange
        var apiResponse = new OpenRouterModelsResponse(
        [
            new OpenRouterModel("openai/gpt-4o", "GPT-4o", "Best model", 128000,
                new OpenRouterPricing("0.000005", "0.000015", null, null),
                new OpenRouterArchitecture("text->text", "GPT", null),
                new OpenRouterTopProvider(128000, 16384, false), null)
        ]);

        var json = JsonSerializer.Serialize(apiResponse, CamelCaseOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].ModelId.Should().Be("openai/gpt-4o");
        result.Value[0].Provider.Should().Be("openrouter");
    }

    [Fact]
    public async Task ExtractAsync_ShouldMapPricingCorrectly_ToDecimalPerMillion()
    {
        // Arrange: "0.000003" per token = $3.00 per million
        var apiResponse = new OpenRouterModelsResponse(
        [
            new OpenRouterModel("test/model", "Test", null, 4096,
                new OpenRouterPricing("0.000003", "0.000015", null, null),
                new OpenRouterArchitecture("text->text", null, null),
                null, null)
        ]);

        var json = JsonSerializer.Serialize(apiResponse, CamelCaseOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.Value![0].Metrics.PricePerMillion.Should().Be(3.00m);
    }

    [Fact]
    public async Task ExtractAsync_ShouldFilterOutNonTextModels()
    {
        // Arrange
        var apiResponse = new OpenRouterModelsResponse(
        [
            new OpenRouterModel("text/model", "Text", null, 4096,
                new OpenRouterPricing("0.000001", null, null, null),
                new OpenRouterArchitecture("text->text", null, null), null, null),
            new OpenRouterModel("image/model", "Image", null, 4096,
                new OpenRouterPricing("0.000001", null, null, null),
                new OpenRouterArchitecture("image->image", null, null), null, null)
        ]);

        var json = JsonSerializer.Serialize(apiResponse, CamelCaseOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.Value.Should().HaveCount(1);
        result.Value![0].ModelId.Should().Be("text/model");
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyResponse_ShouldReturnEmptyList()
    {
        // Arrange
        var apiResponse = new OpenRouterModelsResponse([]);
        var json = JsonSerializer.Serialize(apiResponse, CamelCaseOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_WithHttpError_ShouldReturnFailResult()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server Error")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter HTTP error");
    }

    [Fact]
    public async Task ExtractAsync_ShouldGenerateConsistentDataHash()
    {
        // Arrange
        var apiResponse = new OpenRouterModelsResponse(
        [
            new OpenRouterModel("test/hash-model", "Hash Test", null, 4096,
                new OpenRouterPricing("0.000001", null, null, null),
                new OpenRouterArchitecture("text->text", null, null), null, null)
        ]);

        var json = JsonSerializer.Serialize(apiResponse, CamelCaseOptions);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.Value![0].DataHash.Should().HaveLength(64);
        result.Value[0].DataHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // ── ParsePricePerMillion unit tests ──────────────────────────────────────

    [Theory]
    [InlineData("0.000003", 3.0)]
    [InlineData("0.000015", 15.0)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("invalid", null)]
    public void ParsePricePerMillion_ShouldConvertCorrectly(string? input, double? expected)
    {
        var result = OpenRouterExtractor.ParsePricePerMillion(input);

        if (expected is null)
            result.Should().BeNull();
        else
            result.Should().Be((decimal)expected.Value);
    }

    [Fact]
    public void ParsePricePerMillion_WithZero_ShouldReturnZero()
    {
        var result = OpenRouterExtractor.ParsePricePerMillion("0");
        result.Should().Be(0m);
    }
}

// ── Test Doubles ────────────────────────────────────────────────────────────

/// <summary>Fake HttpMessageHandler que retorna uma resposta pré-definida.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_response);
}

/// <summary>Fake IHttpClientFactory que retorna um HttpClient pré-configurado.</summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    public FakeHttpClientFactory(HttpClient client) => _client = client;

    public HttpClient CreateClient(string name) => _client;
}
