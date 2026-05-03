using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Infrastructure.Extractors;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Tests.Infrastructure;

public sealed class ArtificialAnalysisExtractorTests
{
    [Fact]
    public async Task ExtractAsync_WhenSourceIsDisabled_ShouldSkipHttpCall()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://artificialanalysis.ai/api/v2/")
        };

        await using var dbContext = CreateDbContext();

        var extractor = new ArtificialAnalysisExtractor(
            httpClient,
            Options.Create(new SourceOptions
            {
                ArtificialAnalysis = new ArtificialAnalysisSourceOptions
                {
                    Enabled = false,
                    BaseUrl = "https://artificialanalysis.ai/api/v2",
                    ModelsPath = "data/llms/models"
                }
            }),
            NullLogger<ArtificialAnalysisExtractor>.Instance,
            dbContext);

        var result = await extractor.ExtractAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ExtractAsync_WhenSourceIsEnabled_ShouldUseConfiguredModelsPath()
    {
        const string payload = """
                {
                    "status": 200,
                    "data": [
                        {
                            "id": "2dad8957-4c16-4e74-bf2d-8b21514e0ae9",
                            "slug": "o3-mini",
                            "evaluations": {
                                "artificial_analysis_intelligence_index": 62.9
                            },
                            "pricing": {
                                "price_1m_blended_3_to_1": 1.925
                            },
                            "median_output_tokens_per_second": 153.831,
                            "median_time_to_first_token_seconds": 14.939
                        }
                    ]
                }
        """;

        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://artificialanalysis.ai/api/v2/")
        };

        await using var dbContext = CreateDbContext();

        var extractor = new ArtificialAnalysisExtractor(
            httpClient,
            Options.Create(new SourceOptions
            {
                ArtificialAnalysis = new ArtificialAnalysisSourceOptions
                {
                    Enabled = true,
                    BaseUrl = "https://artificialanalysis.ai/api/v2",
                    ModelsPath = "custom/models"
                }
            }),
            NullLogger<ArtificialAnalysisExtractor>.Instance,
            dbContext);

        var result = await extractor.ExtractAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].Provider.Should().Be("ArtificialAnalysis");
        result.Value[0].Metrics.EloRating.Should().Be(62.9);
        result.Value[0].Metrics.PricePerMillion.Should().Be(1.925m);
        result.Value[0].Metrics.TokensPerSec.Should().Be(153.831);
        result.Value[0].Metrics.TtftMs.Should().Be(14939d);
        handler.RequestCount.Should().Be(1);
        handler.LastRequestUri.Should().Be(new Uri("https://artificialanalysis.ai/api/v2/custom/models"));
    }

    private static NewsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NewsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NewsDbContext(options);
    }

    private sealed class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response = response;

        public Uri? LastRequestUri { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_response);
        }
    }
}