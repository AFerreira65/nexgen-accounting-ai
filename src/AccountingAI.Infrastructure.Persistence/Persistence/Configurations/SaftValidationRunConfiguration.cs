using AccountingAI.Domain.Saft;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class SaftValidationRunConfiguration : IEntityTypeConfiguration<SaftValidationRun>
{
    public void Configure(EntityTypeBuilder<SaftValidationRun> builder)
    {
        builder.ToTable("SaftValidationRun", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.FileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.AuditFileVersion).HasMaxLength(16);
        builder.Property(item => item.IssuesJson).IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(item => new { item.CompanyId, item.CreatedAtUtc });
    }
}
