using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.AiNewsDelivery.Configuration;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Enums;
using Platform.AiNewsDelivery.Domain.Models;
using Platform.AiNewsDelivery.Infrastructure;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Tests.Infrastructure;

public sealed class SnapshotComparerTests : IDisposable
{
    private readonly NewsDbContext _dbContext;
    private readonly SnapshotComparer _comparer;

    public SnapshotComparerTests()
    {
        var options = new DbContextOptionsBuilder<NewsDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new NewsDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        var workerOptions = Options.Create(new NewsWorkerOptions
        {
            Thresholds = new ThresholdOptions
            {
                PriceDropSignificantPercent = 20.0,
                PriceChangePercent = 10.0
            }
        });

        _comparer = new SnapshotComparer(
            _dbContext,
            workerOptions,
            NullLogger<SnapshotComparer>.Instance);
    }

    [Fact]
    public async Task CompareAsync_WithNoPreviousSnapshot_ShouldReturnNewModelChange()
    {
        // Arrange
        var current = CreateCanonicalSnapshot("some/new-model", 10.0m, 1000);

        // Act
        var result = await _comparer.CompareAsync([current]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        var change = result.Value!.Single();

        change.ChangeType.Should().Be(ChangeType.NewModel);
        change.Severity.Should().Be(ChangeSeverity.Digest); // normal model -> Digest
        change.ModelId.Should().Be("some/new-model");
    }

    [Fact]
    public async Task CompareAsync_WithWatchlistNewModel_ShouldReturnBreakingSeverity()
    {
        // Arrange
        var current = CreateCanonicalSnapshot("openai/gpt-5", 10.0m, 1000);

        // Act
        var result = await _comparer.CompareAsync([current]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var change = result.Value!.Single();
        change.ChangeType.Should().Be(ChangeType.NewModel);
        change.Severity.Should().Be(ChangeSeverity.Breaking); // watchlist -> Breaking
    }

    [Fact]
    public async Task CompareAsync_WithPriceDropAboveSignificantThreshold_ShouldReturnBreaking()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-1);
        var oldMetrics = new Metrics { PricePerMillion = 10.0m }; // $10 -> $7.5 = 25% drop
        await SeedSnapshotAsync("model1", oldMetrics, oldDate);

        var current = CreateCanonicalSnapshot("model1", 7.5m, 1000);

        // Act
        var result = await _comparer.CompareAsync([current]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var change = result.Value!.Single();
        change.ChangeType.Should().Be(ChangeType.PriceChange);
        change.DeltaPercent.Should().BeApproximately(25.0, 0.01);
        change.Severity.Should().Be(ChangeSeverity.Breaking);
    }

    [Fact]
    public async Task CompareAsync_WithPriceDropBelowSignificantButAboveChange_ShouldReturnDigest()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-1);
        var oldMetrics = new Metrics { PricePerMillion = 10.0m }; // $10 -> $8.5 = 15% drop (10% < 15% < 20%)
        await SeedSnapshotAsync("model2", oldMetrics, oldDate);

        var current = CreateCanonicalSnapshot("model2", 8.5m, 1000);

        // Act
        var result = await _comparer.CompareAsync([current]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var change = result.Value!.Single();
        change.ChangeType.Should().Be(ChangeType.PriceChange);
        change.Severity.Should().Be(ChangeSeverity.Digest);
    }

    [Fact]
    public async Task CompareAsync_WithMinorPriceDrop_ShouldReturnSilent()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-1);
        var oldMetrics = new Metrics { PricePerMillion = 10.0m }; // $10 -> $9.5 = 5% drop
        await SeedSnapshotAsync("model3", oldMetrics, oldDate);

        var current = CreateCanonicalSnapshot("model3", 9.5m, 1000);

        // Act
        var result = await _comparer.CompareAsync([current]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var change = result.Value!.Single();
        change.ChangeType.Should().Be(ChangeType.PriceChange);
        change.Severity.Should().Be(ChangeSeverity.Silent);
    }

    [Fact]
    public async Task CompareAsync_WithContextWindowIncrease_ShouldReturnDigest()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-1);
        var oldMetrics = new Metrics { PricePerMillion = 10.0m, ContextWindow = 4000 };
        await SeedSnapshotAsync("model4", oldMetrics, oldDate);

        var current = CreateCanonicalSnapshot("model4", 10.0m, 8000); // 100% increase

        // Act
        var result = await _comparer.CompareAsync([current]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var change = result.Value!.Single();
        change.ChangeType.Should().Be(ChangeType.ContextWindowChange);
        change.Severity.Should().Be(ChangeSeverity.Digest);
    }

    private async Task SeedSnapshotAsync(string modelId, Metrics metrics, DateTime collectedAt)
    {
        _dbContext.ModelSnapshots.Add(new ModelSnapshot
        {
            ModelId = modelId,
            Provider = "test_provider",
            CollectedAt = collectedAt,
            RawData = "{}",
            DataHash = "hash",
            MetricsJson = JsonSerializer.Serialize(metrics),
            MetadataJson = "{}"
        });
        await _dbContext.SaveChangesAsync();
    }

    private static CanonicalSnapshot CreateCanonicalSnapshot(string modelId, decimal price, int context)
    {
        return new CanonicalSnapshot(
            ModelId: modelId,
            Provider: "test_provider",
            Metrics: new Metrics { PricePerMillion = price, ContextWindow = context },
            Metadata: new ModelMetadata { IsNew = false },
            CollectedAt: DateTime.UtcNow
        );
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }
}
