using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.AiNewsDelivery.Domain.Entities;

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Configurations;

internal sealed class ModelSnapshotConfiguration : IEntityTypeConfiguration<ModelSnapshot>
{
    public void Configure(EntityTypeBuilder<ModelSnapshot> builder)
    {
        builder.ToTable("model_snapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.ModelId).HasColumnName("model_id").IsRequired().HasMaxLength(512);
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired().HasMaxLength(128);
        builder.Property(x => x.CollectedAt).HasColumnName("collected_at").IsRequired();
        builder.Property(x => x.DataHash).HasColumnName("data_hash").IsRequired().HasMaxLength(64);
        builder.Property(x => x.RawData).HasColumnName("raw_data").IsRequired();
        builder.Property(x => x.MetricsJson).HasColumnName("metrics_json").IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").IsRequired();

        builder.HasIndex(x => new { x.ModelId, x.Provider, x.CollectedAt })
            .IsUnique()
            .HasDatabaseName("ux_snapshots_model_provider_collected");

        builder.HasIndex(x => new { x.ModelId, x.CollectedAt })
            .HasDatabaseName("ix_snapshots_model_collected_desc");
    }
}
