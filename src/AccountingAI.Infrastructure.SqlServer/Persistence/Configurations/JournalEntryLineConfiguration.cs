using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        builder.ToTable("JournalEntryLines", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Description).HasMaxLength(300).IsRequired();
        builder.Property(item => item.Debit).HasPrecision(18, 2);
        builder.Property(item => item.Credit).HasPrecision(18, 2);
        builder.HasIndex(item => new { item.CompanyId, item.JournalEntryId, item.Sequence }).IsUnique();
        builder.HasOne(item => item.JournalEntry)
            .WithMany(item => item.Lines)
            .HasForeignKey(item => new { item.CompanyId, item.JournalEntryId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Account)
            .WithMany(item => item.EntryLines)
            .HasForeignKey(item => new { item.CompanyId, item.AccountId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Party)
            .WithMany()
            .HasForeignKey(item => item.PartyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.VatCode)
            .WithMany()
            .HasForeignKey(item => item.VatCodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.CostCenter)
            .WithMany()
            .HasForeignKey(item => item.CostCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
