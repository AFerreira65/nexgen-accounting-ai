using AccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class CompanyAiSettingsConfiguration : IEntityTypeConfiguration<CompanyAiSettings>
{
    public void Configure(EntityTypeBuilder<CompanyAiSettings> builder)
    {
        builder.ToTable("CompanyAiSettings", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.CompanyId).IsUnique();
    }
}
