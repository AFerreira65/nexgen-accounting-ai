using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("Accounts", "accounting");
        builder.HasKey(item => item.Id);
        builder.HasAlternateKey(item => new { item.CompanyId, item.Id });
        builder.Property(item => item.Code).HasMaxLength(32).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ParentCode).HasMaxLength(32);
        builder.Property(item => item.AllowsPosting).HasDefaultValue(true);
        builder.Property(item => item.SourceTemplateVersion).HasMaxLength(64);
        builder.Property(item => item.TaxonomyReference).HasMaxLength(1).IsFixedLength();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
