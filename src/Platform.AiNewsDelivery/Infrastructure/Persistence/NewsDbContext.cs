using MassTransit;
using Microsoft.EntityFrameworkCore;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Infrastructure.Persistence.Configurations;

namespace Platform.AiNewsDelivery.Infrastructure.Persistence;

public sealed class NewsDbContext : DbContext
{
    public NewsDbContext(DbContextOptions<NewsDbContext> options) : base(options) { }

    public DbSet<ModelSnapshot> ModelSnapshots => Set<ModelSnapshot>();
    public DbSet<ModelChange> ModelChanges => Set<ModelChange>();
    public DbSet<WorkerRun> WorkerRuns => Set<WorkerRun>();
    public DbSet<ModelAlias> ModelAliases => Set<ModelAlias>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ModelSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new ModelChangeConfiguration());
        modelBuilder.ApplyConfiguration(new WorkerRunConfiguration());
        modelBuilder.ApplyConfiguration(new ModelAliasConfiguration());
        
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        
        base.OnModelCreating(modelBuilder);
    }
}
