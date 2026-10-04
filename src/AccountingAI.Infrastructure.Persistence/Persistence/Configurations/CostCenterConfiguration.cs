using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.ToTable("CostCenters", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(CostCenter.CodeMaxLength).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(CostCenter.NameMaxLength).IsRequired();
        builder.Property(item => item.IsActive).HasDefaultValue(true);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
