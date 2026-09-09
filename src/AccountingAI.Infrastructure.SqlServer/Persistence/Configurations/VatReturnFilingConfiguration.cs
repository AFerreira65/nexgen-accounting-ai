using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class VatReturnFilingConfiguration : IEntityTypeConfiguration<VatReturnFiling>
{
    public void Configure(EntityTypeBuilder<VatReturnFiling> builder)
    {
        builder.ToTable("VatReturnFiling", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.PriorPeriodCredit).HasPrecision(18, 2);
        builder.Property(item => item.SnapshotJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.PeriodFrom, item.PeriodTo }).IsUnique();
    }
}
