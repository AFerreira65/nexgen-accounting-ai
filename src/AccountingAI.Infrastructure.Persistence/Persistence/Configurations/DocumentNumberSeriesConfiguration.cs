using AccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class DocumentNumberSeriesConfiguration
    : IEntityTypeConfiguration<DocumentNumberSeries>
{
    public void Configure(EntityTypeBuilder<DocumentNumberSeries> builder)
    {
        builder.ToTable("DocumentNumberSeries", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Series).HasMaxLength(24).IsRequired();
        builder.Property(item => item.NextNumber).IsRequired();
        builder.Property(item => item.Width).IsRequired().HasDefaultValue(DocumentNumberSeries.DefaultWidth);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Series, item.Year }).IsUnique();
    }
}
