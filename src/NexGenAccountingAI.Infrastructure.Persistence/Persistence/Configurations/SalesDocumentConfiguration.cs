using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Billing;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class SalesDocumentConfiguration : IEntityTypeConfiguration<SalesDocument>
{
    public void Configure(EntityTypeBuilder<SalesDocument> builder)
    {
        builder.ToTable("SalesDocuments", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.InvoiceNo).HasMaxLength(SalesDocument.InvoiceNoMaxLength).IsRequired();
        builder.Property(item => item.Atcud).HasMaxLength(60);
        builder.Property(item => item.InvoiceType).HasMaxLength(10).IsRequired();
        builder.Property(item => item.CustomerTaxId).HasMaxLength(30).IsRequired();
        builder.Property(item => item.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.CustomerCountry).HasMaxLength(2);
        builder.Property(item => item.NetTotal).HasPrecision(18, 2);
        builder.Property(item => item.TaxPayable).HasPrecision(18, 2);
        builder.Property(item => item.GrossTotal).HasPrecision(18, 2);
        builder.Property(item => item.SourceFileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.JournalEntryReference).HasMaxLength(SalesDocument.JournalEntryReferenceMaxLength);
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.OwnsMany(item => item.Taxes, taxes =>
        {
            taxes.ToJson();
            taxes.Property(tax => tax.Percentage).HasPrecision(5, 2);
            taxes.Property(tax => tax.TaxBase).HasPrecision(18, 2);
            taxes.Property(tax => tax.TaxAmount).HasPrecision(18, 2);
        });
        builder.Navigation(item => item.Taxes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(item => new { item.CompanyId, item.InvoiceNo }).IsUnique();
        builder.HasIndex(item => new { item.CompanyId, item.InvoiceDate });
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesEntryRuleConfiguration : IEntityTypeConfiguration<SalesEntryRule>
{
    public void Configure(EntityTypeBuilder<SalesEntryRule> builder)
    {
        builder.ToTable("SalesEntryRules", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.TaxPercentage).HasPrecision(5, 2);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.TaxPercentage }).IsUnique();
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
