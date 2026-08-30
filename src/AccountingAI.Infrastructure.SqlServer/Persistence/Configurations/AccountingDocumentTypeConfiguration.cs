using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class AccountingDocumentTypeConfiguration
    : IEntityTypeConfiguration<AccountingDocumentType>
{
    public void Configure(EntityTypeBuilder<AccountingDocumentType> builder)
    {
        builder.ToTable("AccountingDocumentTypes", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(24).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.SourceTemplateVersion).HasMaxLength(64);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne(item => item.DefaultJournal)
            .WithMany()
            .HasForeignKey(item => new { item.CompanyId, item.DefaultJournalId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
