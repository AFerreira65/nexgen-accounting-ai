using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexGenAccountingAI.Domain.Organizations;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AtGeneralSettingsConfiguration : IEntityTypeConfiguration<AtGeneralSettings>
{
    public void Configure(EntityTypeBuilder<AtGeneralSettings> builder)
    {
        builder.ToTable("AtGeneralSettings", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.CertificateDirectory).HasMaxLength(AtGeneralSettings.PathMaxLength);
        builder.Property(item => item.CertificateFileName).HasMaxLength(AtGeneralSettings.PathMaxLength);
        builder.Property(item => item.PublicKeyDirectory).HasMaxLength(AtGeneralSettings.PathMaxLength);
        builder.Property(item => item.PublicKeyFileName).HasMaxLength(AtGeneralSettings.PathMaxLength);
        builder.Property(item => item.RowVersion).IsRowVersion();
        // Uma só linha por instalação.
        builder.HasIndex(item => item.Key).IsUnique();
    }
}
