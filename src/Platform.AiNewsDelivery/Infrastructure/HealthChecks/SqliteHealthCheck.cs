using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Infrastructure.HealthChecks;

public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly NewsDbContext _dbContext;

    public SqliteHealthCheck(NewsDbContext dbContext) => _dbContext = dbContext;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQLite database is accessible.", data: BuildDiagnosticData())
                : HealthCheckResult.Unhealthy("SQLite database is not accessible.", data: BuildDiagnosticData());
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQLite health check failed.", exception: ex, data: BuildDiagnosticData());
        }
    }

    private Dictionary<string, object> BuildDiagnosticData()
    {
        var connectionString = _dbContext.Database.GetConnectionString() ?? "unknown";
        var dbPath = connectionString.Split(';')
            .FirstOrDefault(s => s.Trim().StartsWith("Data Source", StringComparison.OrdinalIgnoreCase))
            ?.Split('=').LastOrDefault() ?? "unknown";

        return new Dictionary<string, object>
        {
            ["database_path"] = dbPath,
            ["provider"] = "SQLite",
            ["checked_at"] = DateTime.UtcNow
        };
    }
}
