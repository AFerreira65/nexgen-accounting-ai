using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Banking;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.ToTable("BankStatementLines", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.LineKey).HasMaxLength(BankStatementLine.LineKeyMaxLength).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(BankStatementLine.DescriptionMaxLength).IsRequired();
        builder.Property(item => item.Reference).HasMaxLength(BankStatementLine.ReferenceMaxLength);
        builder.Property(item => item.Amount).HasPrecision(18, 2);
        builder.Property(item => item.Balance).HasPrecision(18, 2);
        builder.Property(item => item.ReconciledEntryReference).HasMaxLength(BankStatementLine.EntryReferenceMaxLength);
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.HasIndex(item => new { item.CompanyId, item.AccountId, item.LineKey }).IsUnique();
        builder.HasIndex(item => new { item.CompanyId, item.AccountId, item.BookingDate });
        builder.HasIndex(item => new { item.CompanyId, item.ReconciledEntryReference });
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(item => item.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BankStatementImport>()
            .WithMany()
            .HasForeignKey(item => item.ImportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankStatementImportConfiguration : IEntityTypeConfiguration<BankStatementImport>
{
    public void Configure(EntityTypeBuilder<BankStatementImport> builder)
    {
        builder.ToTable("BankStatementImports", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.FileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.Format).HasMaxLength(20).IsRequired();
        builder.Property(item => item.Iban).HasMaxLength(34);
        builder.Property(item => item.OpeningBalance).HasPrecision(18, 2);
        builder.Property(item => item.ClosingBalance).HasPrecision(18, 2);
        builder.Property(item => item.ImportedAtUtc).HasDefaultValueSql("now()");
        builder.HasIndex(item => new { item.CompanyId, item.AccountId, item.ClosingDate });
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(item => item.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankEntryRuleConfiguration : IEntityTypeConfiguration<BankEntryRule>
{
    public void Configure(EntityTypeBuilder<BankEntryRule> builder)
    {
        builder.ToTable("BankEntryRules", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Pattern).HasMaxLength(BankEntryRule.PatternMaxLength).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Pattern }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EntryTemplate>()
            .WithMany()
            .HasForeignKey(item => item.EntryTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
