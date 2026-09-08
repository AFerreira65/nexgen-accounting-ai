using AccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class ReportTemplateConfiguration : IEntityTypeConfiguration<ReportTemplate>
{
    public void Configure(EntityTypeBuilder<ReportTemplate> builder)
    {
        builder.ToTable("ReportTemplate", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ReportKey).HasMaxLength(ReportTemplate.ReportKeyMaxLength).IsRequired();
        builder.Property(item => item.HeaderLinesJson).IsRequired();
        builder.Property(item => item.Orientation).HasConversion<int>();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.ReportKey }).IsUnique();
    }
}
