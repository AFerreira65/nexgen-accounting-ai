using NexGenAccountingAI.Domain.Efatura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class EFaturaPurchaseConfiguration : IEntityTypeConfiguration<EFaturaPurchase>
{
    public void Configure(EntityTypeBuilder<EFaturaPurchase> builder)
    {
        builder.ToTable("EFaturaPurchase", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SupplierTaxId).HasMaxLength(20).IsRequired();
        builder.Property(item => item.SupplierName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.InvoiceNo).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Atcud).HasMaxLength(60);
        builder.Property(item => item.InvoiceType).HasMaxLength(20).IsRequired();
        builder.Property(item => item.Status).HasMaxLength(40);
        builder.Property(item => item.Sector).HasMaxLength(120);
        builder.Property(item => item.SourceFileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.DocumentKey).HasMaxLength(180).IsRequired();
        builder.Property(item => item.TaxBase).HasPrecision(18, 2);
        builder.Property(item => item.TaxAmount).HasPrecision(18, 2);
        builder.Property(item => item.GrossTotal).HasPrecision(18, 2);
        builder.Property(item => item.DismissedReason).HasMaxLength(EFaturaPurchase.DismissedReasonMaxLength);
        builder.Property(item => item.JournalEntryReference).HasMaxLength(EFaturaPurchase.JournalEntryReferenceMaxLength);
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.HasIndex(item => new { item.CompanyId, item.DocumentKey }).IsUnique();
        builder.HasIndex(item => new { item.CompanyId, item.InvoiceDate });
    }
}
