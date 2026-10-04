using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class SupplierEntryRuleConfiguration : IEntityTypeConfiguration<SupplierEntryRule>
{
    public void Configure(EntityTypeBuilder<SupplierEntryRule> builder)
    {
        builder.ToTable("SupplierEntryRules", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SupplierTaxId).HasMaxLength(SupplierEntryRule.TaxIdMaxLength).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(SupplierEntryRule.DescriptionMaxLength);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.SupplierTaxId }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.EntryTemplate)
            .WithMany()
            .HasForeignKey(item => item.EntryTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
