using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.AiNewsDelivery.Domain.Entities;
using Platform.AiNewsDelivery.Domain.Enums;

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Configurations;

internal sealed class ModelChangeConfiguration : IEntityTypeConfiguration<ModelChange>
{
    public void Configure(EntityTypeBuilder<ModelChange> builder)
    {
        builder.ToTable("model_changes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.ModelId).HasColumnName("model_id").IsRequired().HasMaxLength(512);
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired().HasMaxLength(128).HasDefaultValue("Unknown");
        builder.Property(x => x.ChangeType).HasColumnName("change_type").IsRequired().HasConversion<string>();
        builder.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(128);
        builder.Property(x => x.OldValue).HasColumnName("old_value");
        builder.Property(x => x.NewValue).HasColumnName("new_value");
        builder.Property(x => x.DeltaPercent).HasColumnName("delta_percent");
        builder.Property(x => x.Severity).HasColumnName("severity").IsRequired().HasConversion<string>();
        builder.Property(x => x.DetectedAt).HasColumnName("detected_at").IsRequired();
        builder.Property(x => x.Dispatched).HasColumnName("dispatched").IsRequired().HasDefaultValue(false);
        builder.Property(x => x.DispatchedAt).HasColumnName("dispatched_at");

        builder.HasIndex(x => new { x.DetectedAt, x.Dispatched })
            .HasDatabaseName("ix_changes_detected_dispatched");

        builder.HasIndex(x => new { x.ModelId, x.ChangeType })
            .HasDatabaseName("ix_changes_model_type");
    }
}
