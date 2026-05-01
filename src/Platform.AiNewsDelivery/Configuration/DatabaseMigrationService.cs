using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.AiNewsDelivery.Infrastructure.Persistence;

namespace Platform.AiNewsDelivery.Configuration;

internal sealed partial class DatabaseMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogApplyingMigrations();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NewsDbContext>();

        try
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
            LogMigrationsApplied();
        }
        catch (Exception ex)
        {
            LogMigrationFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Aplicando migrations do EF Core...")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations aplicadas com sucesso. Banco de dados pronto.")]
    private partial void LogMigrationsApplied();

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao aplicar migrations. O serviço pode não funcionar corretamente.")]
    private partial void LogMigrationFailed(Exception ex);
}
