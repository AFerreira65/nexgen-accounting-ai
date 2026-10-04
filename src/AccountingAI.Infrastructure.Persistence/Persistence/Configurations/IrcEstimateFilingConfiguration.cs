using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class IrcEstimateFilingConfiguration : IEntityTypeConfiguration<IrcEstimateFiling>
{
    public void Configure(EntityTypeBuilder<IrcEstimateFiling> builder)
    {
        builder.ToTable("IrcEstimateFiling", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.TaxRate).HasPrecision(6, 4);
        builder.Property(item => item.AdjustmentsJson).IsRequired();
        builder.Property(item => item.SnapshotJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.PeriodFrom, item.PeriodTo }).IsUnique();
    }
}
