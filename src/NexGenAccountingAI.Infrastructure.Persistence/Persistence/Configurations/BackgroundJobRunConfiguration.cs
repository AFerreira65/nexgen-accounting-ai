using NexGenAccountingAI.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class BackgroundJobRunConfiguration : IEntityTypeConfiguration<BackgroundJobRun>
{
    public void Configure(EntityTypeBuilder<BackgroundJobRun> builder)
    {
        builder.ToTable("BackgroundJobRuns", "infrastructure");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.JobKey).HasMaxLength(60).IsRequired();
        builder.Property(item => item.PeriodKey).HasMaxLength(40).IsRequired();
        builder.Property(item => item.Status).HasConversion<int>();
        builder.Property(item => item.Message).HasMaxLength(1000);
        builder.Property(item => item.RowVersion).IsRowVersion();
        // Uma só linha por unidade de trabalho: é o que impede duas execuções da mesma.
        builder.HasIndex(item => new { item.JobKey, item.CompanyId, item.PeriodKey }).IsUnique();
        builder.HasIndex(item => item.StartedAtUtc);
    }
}
