using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class ProvisionMapInputConfiguration : IEntityTypeConfiguration<ProvisionMapInput>
{
    public void Configure(EntityTypeBuilder<ProvisionMapInput> builder)
    {
        builder.ToTable("ProvisionMapInputs", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AccountCode).HasMaxLength(ProvisionMapInput.AccountCodeMaxLength).IsRequired();
        builder.Property(item => item.TaxedOpening).HasPrecision(18, 2);
        builder.Property(item => item.TaxedUsed).HasPrecision(18, 2);
        builder.Property(item => item.TaxedReversed).HasPrecision(18, 2);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Year, item.AccountCode }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
