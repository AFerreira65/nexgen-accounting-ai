using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AccountingJournalConfiguration : IEntityTypeConfiguration<AccountingJournal>
{
    public void Configure(EntityTypeBuilder<AccountingJournal> builder)
    {
        builder.ToTable("Journals", "accounting");
        builder.HasKey(item => item.Id);
        builder.HasAlternateKey(item => new { item.CompanyId, item.Id });
        builder.Property(item => item.Code).HasMaxLength(24).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.SourceTemplateVersion).HasMaxLength(64);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
