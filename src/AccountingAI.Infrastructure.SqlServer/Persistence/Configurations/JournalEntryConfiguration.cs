using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries", "accounting");
        builder.HasKey(item => item.Id);
        builder.HasAlternateKey(item => new { item.CompanyId, item.Id });
        builder.Property(item => item.Reference).HasMaxLength(48).IsRequired();
        builder.Property(item => item.EntryDate).HasColumnType("date");
        builder.Property(item => item.DocumentReference).HasMaxLength(240).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(500).IsRequired();
        builder.Property(item => item.Status).HasConversion<int>();
        builder.Property(item => item.TotalAmount).HasPrecision(18, 2);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Reference }).IsUnique();
        builder.HasIndex(item => new { item.CompanyId, item.EntryDate });
        builder.HasIndex(item => new { item.CompanyId, item.DocumentTypeId });
        builder.HasOne(item => item.Journal)
            .WithMany(item => item.Entries)
            .HasForeignKey(item => new { item.CompanyId, item.JournalId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.DocumentType)
            .WithMany()
            .HasForeignKey(item => new { item.CompanyId, item.DocumentTypeId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
