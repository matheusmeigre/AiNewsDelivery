using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Enums;

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Configurations;

internal sealed class WorkerRunConfiguration : IEntityTypeConfiguration<WorkerRun>
{
    public void Configure(EntityTypeBuilder<WorkerRun> builder)
    {
        builder.ToTable("worker_runs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>();
        builder.Property(x => x.ModelsCollected).HasColumnName("models_collected").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.ChangesDetected).HasColumnName("changes_detected").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.ErrorsLog).HasColumnName("errors_log");

        builder.Ignore(x => x.Duration);
    }
}
