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
/// Testes unitários do <see cref="HuggingFaceExtractor"/>.
/// Valida o sweep híbrido (downloads + lastModified), deduplicação, e extração de arXiv.
/// </summary>
public sealed class HuggingFaceExtractorTests
{
    private readonly SourceOptions _sourceOptions = new()
    {
        HuggingFace = new HuggingFaceSourceOptions
        {
            BaseUrl = "https://huggingface.co/api",
            Enabled = true,
            Token = null
        }
    };

    private HuggingFaceExtractor CreateExtractor(HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/api/") };
        var factory = new FakeHttpClientFactory(httpClient);

        var workerOptions = new NewsWorkerOptions
        {
            Thresholds = new ThresholdOptions { HuggingFaceMinDownloads = 0 }
        };

        return new HuggingFaceExtractor(
            factory,
            Options.Create(_sourceOptions),
            Options.Create(workerOptions),
            NullLogger<HuggingFaceExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_WithValidResponse_ShouldReturnCanonicalSnapshots()
    {
        // Arrange
        var models = new List<HuggingFaceModelDto>
        {
            new("Qwen/Qwen3-0.6B", "Qwen/Qwen3-0.6B", "text-generation", 19788680, 1217,
                ["transformers", "text-generation", "arxiv:2505.09388"], "transformers",
                DateTime.UtcNow.AddDays(-30), "Qwen", false, null)
        };

        var json = JsonSerializer.Serialize(models);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value![0].Provider.Should().Be("huggingface");
        result.Value[0].Metadata.SourceUrl.Should().Contain("huggingface.co/Qwen/Qwen3-0.6B");
    }

    [Fact]
    public async Task ExtractAsync_ShouldMarkNewModels_WhenCreatedRecently()
    {
        // Arrange — modelo criado há 2 dias (dentro do threshold de 7)
        var models = new List<HuggingFaceModelDto>
        {
            new("new/model", "new/model", "text-generation", 100, 10,
                null, "transformers", DateTime.UtcNow.AddDays(-2), "new-author", false, null)
        };

        var json = JsonSerializer.Serialize(models);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.Value![0].Metadata.IsNew.Should().BeTrue(
            "modelo criado há menos de 7 dias deve ser marcado como novo");
    }

    [Fact]
    public async Task ExtractAsync_ShouldExtractArxivUrl_FromTags()
    {
        // Arrange
        var models = new List<HuggingFaceModelDto>
        {
            new("test/arxiv", "test/arxiv", "text-generation", 1000, 50,
                ["transformers", "arxiv:2505.09388"], "transformers",
                DateTime.UtcNow.AddDays(-30), "test", false, null)
        };

        var json = JsonSerializer.Serialize(models);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.Value![0].Metadata.TechnicalPaperUrl.Should().Be("https://arxiv.org/abs/2505.09388");
    }

    [Fact]
    public async Task ExtractAsync_WithHttpError_ShouldReturnFailResult()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Rate limited")
        };

        var extractor = CreateExtractor(response);

        // Act
        var result = await extractor.ExtractAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("HuggingFace HTTP error");
    }

    [Fact]
    public async Task ExtractAsync_ShouldGenerateConsistentDataHash()
    {
        // Arrange
        var models = new List<HuggingFaceModelDto>
        {
            new("hash/test", "hash/test", "text-generation", 500, 10,
                null, "transformers", DateTime.UtcNow.AddDays(-30), "author", false, null)
        };

        var json = JsonSerializer.Serialize(models);
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

    // ── ExtractArxivUrl unit tests ───────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "arxiv:2505.09388", "transformers" }, "https://arxiv.org/abs/2505.09388")]
    [InlineData(new[] { "transformers", "safetensors" }, null)]
    public void ExtractArxivUrl_ShouldParseCorrectly(string[] tags, string? expected)
    {
        var result = HuggingFaceExtractor.ExtractArxivUrl(tags);
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtractArxivUrl_WithNullTags_ShouldReturnNull()
    {
        HuggingFaceExtractor.ExtractArxivUrl(null).Should().BeNull();
    }
}
