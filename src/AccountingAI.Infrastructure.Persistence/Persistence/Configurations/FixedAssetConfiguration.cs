using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class FixedAssetConfiguration : IEntityTypeConfiguration<FixedAsset>
{
    public void Configure(EntityTypeBuilder<FixedAsset> builder)
    {
        builder.ToTable("FixedAssets", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(FixedAsset.CodeMaxLength).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(FixedAsset.DescriptionMaxLength).IsRequired();
        builder.Property(item => item.AcquisitionValue).HasPrecision(18, 2);
        builder.Property(item => item.ResidualValue).HasPrecision(18, 2);
        builder.Property(item => item.AnnualRate).HasPrecision(7, 4);
        builder.Property(item => item.IsActive).HasDefaultValue(true);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.Ignore(item => item.DepreciableValue);
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.AssetAccount)
            .WithMany()
            .HasForeignKey(item => item.AssetAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.AccumulatedAccount)
            .WithMany()
            .HasForeignKey(item => item.AccumulatedAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ExpenseAccount)
            .WithMany()
            .HasForeignKey(item => item.ExpenseAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
