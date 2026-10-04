using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> builder)
    {
        builder.ToTable("BudgetLines", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AccountCode).HasMaxLength(BudgetLine.AccountCodeMaxLength).IsRequired();
        builder.Property(item => item.Amount).HasPrecision(18, 2);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Year, item.AccountCode, item.CostCenterId, item.Month }).IsUnique().HasFilter(null);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.CostCenter)
            .WithMany()
            .HasForeignKey(item => item.CostCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
