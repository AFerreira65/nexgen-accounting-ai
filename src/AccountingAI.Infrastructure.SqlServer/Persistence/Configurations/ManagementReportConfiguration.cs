using AccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class ManagementReportConfiguration : IEntityTypeConfiguration<ManagementReport>
{
    public void Configure(EntityTypeBuilder<ManagementReport> builder)
    {
        builder.ToTable("ManagementReport", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Kind).HasMaxLength(ManagementReport.KindMaxLength).IsRequired();
        builder.Property(item => item.Question).HasMaxLength(ManagementReport.QuestionMaxLength);
        builder.Property(item => item.Model).HasMaxLength(ManagementReport.ModelMaxLength).IsRequired();
        builder.Property(item => item.DossierJson).IsRequired();
        builder.Property(item => item.ResultJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.PeriodFrom, item.PeriodTo });
    }
}
