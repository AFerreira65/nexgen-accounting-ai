using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class CompanyAtCredentialsConfiguration : IEntityTypeConfiguration<CompanyAtCredentials>
{
    public void Configure(EntityTypeBuilder<CompanyAtCredentials> builder)
    {
        builder.ToTable("CompanyAtCredentials", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SubUser).HasMaxLength(CompanyAtCredentials.SubUserMaxLength).IsRequired();
        builder.Property(item => item.EncryptedPassword).IsRequired();
        builder.Property(item => item.Periodicity).HasConversion<int>();
        builder.Property(item => item.Environment).HasConversion<int>();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.CompanyId).IsUnique();
    }
}
