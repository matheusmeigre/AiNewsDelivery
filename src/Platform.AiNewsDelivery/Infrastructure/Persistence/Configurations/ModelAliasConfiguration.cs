using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.AiNewsDelivery.Domain.Entities;

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Configurations;

public sealed class ModelAliasConfiguration : IEntityTypeConfiguration<ModelAlias>
{
    public void Configure(EntityTypeBuilder<ModelAlias> builder)
    {
        builder.ToTable("model_aliases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderId)
            .HasColumnName("provider_id")
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.CanonicalId)
            .HasColumnName("canonical_id")
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(x => x.ProviderId).IsUnique();
    }
}
