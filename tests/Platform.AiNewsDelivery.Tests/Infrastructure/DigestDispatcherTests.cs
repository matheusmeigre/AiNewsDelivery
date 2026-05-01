using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Enums;
using Platform.AiNewsDelivery.Infrastructure.Dispatchers;
using Platform.AiNewsDelivery.Infrastructure.Persistence;
using Platform.Contracts;

namespace Platform.AiNewsDelivery.Tests.Infrastructure;

public sealed class DigestDispatcherTests : IDisposable
{
    private readonly NewsDbContext _dbContext;
    private readonly Mock<IBus> _busMock;
    private readonly DigestDispatcher _dispatcher;

    public DigestDispatcherTests()
    {
        var options = new DbContextOptionsBuilder<NewsDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new NewsDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _busMock = new Mock<IBus>();

        _dispatcher = new DigestDispatcher(
            _dbContext,
            _busMock.Object,
            NullLogger<DigestDispatcher>.Instance);
    }

    [Fact]
    public async Task DispatchPendingChangesAsync_ShouldNotPublish_WhenNoPendingChanges()
    {
        // Arrange
        // (Banco vazio)

        // Act
        await _dispatcher.DispatchPendingChangesAsync();

        // Assert
        _busMock.Verify(b => b.Publish(It.IsAny<SendLlmDigestCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingChangesAsync_ShouldNotPublish_WhenOnlySilentChanges()
    {
        // Arrange
        _dbContext.ModelChanges.Add(new ModelChange
        {
            ModelId = "test/model",
            ChangeType = ChangeType.PriceChange,
            Severity = ChangeSeverity.Silent,
            DetectedAt = DateTime.UtcNow,
            Dispatched = false
        });
        await _dbContext.SaveChangesAsync();

        // Act
        await _dispatcher.DispatchPendingChangesAsync();

        // Assert
        _busMock.Verify(b => b.Publish(It.IsAny<SendLlmDigestCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingChangesAsync_ShouldPublishAndMarkDispatched_WhenPendingChangesExist()
    {
        // Arrange
        var change1 = new ModelChange
        {
            ModelId = "test/model-1",
            ChangeType = ChangeType.NewModel,
            Severity = ChangeSeverity.Digest,
            DetectedAt = DateTime.UtcNow,
            Dispatched = false
        };
        var change2 = new ModelChange
        {
            ModelId = "test/model-2",
            ChangeType = ChangeType.PriceChange,
            Severity = ChangeSeverity.Breaking,
            FieldName = "PricePerMillion",
            OldValue = "10.0",
            NewValue = "5.0",
            DeltaPercent = 50.0,
            DetectedAt = DateTime.UtcNow,
            Dispatched = false
        };

        _dbContext.ModelChanges.AddRange(change1, change2);
        await _dbContext.SaveChangesAsync();

        // Act
        await _dispatcher.DispatchPendingChangesAsync();

        // Assert
        _busMock.Verify(b => b.Publish(
            It.Is<SendLlmDigestCommand>(c => c.Changes.Count == 2), 
            It.IsAny<CancellationToken>()), 
            Times.Once);

        var dbChanges = await _dbContext.ModelChanges.ToListAsync();
        dbChanges.Should().AllSatisfy(c => c.Dispatched.Should().BeTrue());
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }
}
