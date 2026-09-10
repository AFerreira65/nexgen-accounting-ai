using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class AccountVatClassificationConfiguration : IEntityTypeConfiguration<AccountVatClassification>
{
    public void Configure(EntityTypeBuilder<AccountVatClassification> builder)
    {
        builder.ToTable("AccountVatClassification", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Rate).HasConversion<int>();
        builder.Property(item => item.Nature).HasConversion<int>();
        builder.Property(item => item.Region).HasConversion<int>();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.AccountId).IsUnique();
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(item => item.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
