using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Tests.Infrastructure;

/// <summary>
/// Testes de integração do NewsDbContext usando SQLite em memória.
/// Usa o provider SQLite (não o InMemory do EF) para garantir que constraints,
/// índices e enums-como-string sejam validados corretamente.
/// </summary>
public sealed class NewsDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly NewsDbContext _context;

    public NewsDbContextTests()
    {
        // SQLite em memória com conexão compartilhada (evita fechar antes dos testes)
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<NewsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new NewsDbContext(options);
        _context.Database.EnsureCreated();
    }

    // ── Schema: tabelas existem ──────────────────────────────────────────────

    [Fact]
    public async Task Database_ShouldHaveModelSnapshotsTable()
    {
        var canConnect = await _context.Database.CanConnectAsync();
        canConnect.Should().BeTrue();

        var count = await _context.ModelSnapshots.CountAsync();
        count.Should().Be(0, "tabela criada e vazia inicialmente");
    }

    [Fact]
    public async Task Database_ShouldHaveModelChangesTable()
    {
        var count = await _context.ModelChanges.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task Database_ShouldHaveWorkerRunsTable()
    {
        var count = await _context.WorkerRuns.CountAsync();
        count.Should().Be(0);
    }

    // ── ModelSnapshot: CRUD e constraints ───────────────────────────────────

    [Fact]
    public async Task ModelSnapshot_ShouldPersistAndRetrieve_WithCorrectMapping()
    {
        // Arrange
        var snapshot = new ModelSnapshot
        {
            ModelId = "anthropic/claude-3-5-sonnet",
            Provider = "openrouter",
            CollectedAt = new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            DataHash = new string('a', 64),
            RawData = """{"id":"anthropic/claude-3-5-sonnet","pricing":{"prompt":"0.000003"}}""",
            MetricsJson = "{}",
            MetadataJson = "{}"
        };

        // Act
        _context.ModelSnapshots.Add(snapshot);
        await _context.SaveChangesAsync();

        // Assert
        var persisted = await _context.ModelSnapshots.SingleAsync();
        persisted.Id.Should().BeGreaterThan(0);
        persisted.ModelId.Should().Be("anthropic/claude-3-5-sonnet");
        persisted.Provider.Should().Be("openrouter");
        persisted.DataHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task ModelSnapshot_UniqueConstraint_ShouldPreventDuplicates()
    {
        // Arrange
        var collectedAt = DateTime.UtcNow;
        var snapshot1 = new ModelSnapshot
        {
            ModelId = "openai/gpt-4o", Provider = "openrouter",
            CollectedAt = collectedAt, DataHash = new string('b', 64), RawData = "{}",
            MetricsJson = "{}", MetadataJson = "{}"
        };
        var snapshot2 = new ModelSnapshot
        {
            ModelId = "openai/gpt-4o", Provider = "openrouter",
            CollectedAt = collectedAt, DataHash = new string('c', 64), RawData = "{}",
            MetricsJson = "{}", MetadataJson = "{}"
        };

        _context.ModelSnapshots.Add(snapshot1);
        await _context.SaveChangesAsync();

        _context.ModelSnapshots.Add(snapshot2);

        // Act & Assert
        var act = async () => await _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "UNIQUE(model_id, provider, collected_at) deve ser violado");
    }

    // ── ModelChange: enum como string ────────────────────────────────────────

    [Fact]
    public async Task ModelChange_ShouldPersist_EnumsAsReadableStrings()
    {
        // Arrange
        var change = new ModelChange
        {
            ModelId = "meta-llama/llama-3-70b-instruct",
            ChangeType = ChangeType.PriceChange,
            FieldName = "pricePerMillion",
            OldValue = "3.00",
            NewValue = "2.50",
            DeltaPercent = -16.67,
            Severity = ChangeSeverity.Digest,
            DetectedAt = DateTime.UtcNow,
            Dispatched = false
        };

        // Act
        _context.ModelChanges.Add(change);
        await _context.SaveChangesAsync();

        // Verifica o valor raw no SQLite via ADO.NET (confirma serialização como string)
        // Não usamos SqlQueryRaw<string> do EF Core pois ele exige coluna "Value"
        var rawValue = string.Empty;
        var conn = _context.Database.GetDbConnection();
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT change_type FROM model_changes LIMIT 1";
            rawValue = (string?)await cmd.ExecuteScalarAsync() ?? string.Empty;
        }

        // Assert
        rawValue.Should().Be("PriceChange",
            "enums devem ser armazenados como strings para legibilidade no SQLite");
    }

    // ── WorkerRun: audit trail ───────────────────────────────────────────────

    [Fact]
    public async Task WorkerRun_ShouldPersist_LifecycleTransition()
    {
        // Arrange — Running
        var run = new WorkerRun
        {
            StartedAt = DateTime.UtcNow,
            Status = WorkerRunStatus.Running
        };
        _context.WorkerRuns.Add(run);
        await _context.SaveChangesAsync();

        run.Id.Should().BeGreaterThan(0);

        // Act — Success
        run.FinishedAt = DateTime.UtcNow;
        run.Status = WorkerRunStatus.Success;
        run.ModelsCollected = 150;
        run.ChangesDetected = 3;
        await _context.SaveChangesAsync();

        // Assert
        var persisted = await _context.WorkerRuns.SingleAsync(r => r.Id == run.Id);
        persisted.Status.Should().Be(WorkerRunStatus.Success);
        persisted.ModelsCollected.Should().Be(150);
        persisted.Duration.Should().NotBeNull();
        persisted.Duration!.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task WorkerRun_Duration_ShouldNotBePersisted_ButComputedCorrectly()
    {
        // Arrange
        var start = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(5).AddSeconds(30);

        var run = new WorkerRun
        {
            StartedAt = start,
            FinishedAt = end,
            Status = WorkerRunStatus.Success
        };
        _context.WorkerRuns.Add(run);
        await _context.SaveChangesAsync();

        // Act
        var persisted = await _context.WorkerRuns.SingleAsync();

        // Assert
        persisted.Duration.Should().Be(TimeSpan.FromSeconds(330),
            "5 minutos e 30 segundos = 330 segundos");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
