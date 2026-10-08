using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class CompanyAiBudgetConfiguration : IEntityTypeConfiguration<CompanyAiBudget>
{
    public void Configure(EntityTypeBuilder<CompanyAiBudget> builder)
    {
        builder.ToTable("CompanyAiBudgets", "reporting");
        builder.HasKey(item => item.CompanyId);
        builder.Property(item => item.CompanyId).ValueGeneratedNever();
        builder.Property(item => item.MonthlyAllowanceUsd).HasPrecision(12, 4);
        builder.Property(item => item.ExtraCreditUsd).HasPrecision(12, 4);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AiCreditTopUpConfiguration : IEntityTypeConfiguration<AiCreditTopUp>
{
    public void Configure(EntityTypeBuilder<AiCreditTopUp> builder)
    {
        builder.ToTable("AiCreditTopUps", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AmountUsd).HasPrecision(12, 4);
        builder.Property(item => item.Note).HasMaxLength(300);
        builder.HasIndex(item => new { item.CompanyId, item.CreatedAtUtc });
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("AiUsageRecords", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Feature).HasConversion<int>();
        builder.Property(item => item.Model).HasMaxLength(80).IsRequired();
        builder.Property(item => item.CostUsd).HasPrecision(14, 6);
        builder.HasIndex(item => new { item.CompanyId, item.CreatedAtUtc });
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
