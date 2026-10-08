using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AiGeneralSettingsConfiguration : IEntityTypeConfiguration<AiGeneralSettings>
{
    public void Configure(EntityTypeBuilder<AiGeneralSettings> builder)
    {
        builder.ToTable("AiGeneralSettings", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.EncryptedApiKey).HasMaxLength(2000);
        builder.Property(item => item.KeyHint).HasMaxLength(16);
        builder.Property(item => item.DefaultMonthlyAllowanceUsd).HasPrecision(12, 4);
        builder.Property(item => item.ChatModel).HasMaxLength(80);
        builder.Property(item => item.ReportModel).HasMaxLength(80);
        builder.Property(item => item.QuickModel).HasMaxLength(80);
        builder.Property(item => item.PricesJson).HasMaxLength(4000);
        builder.Property(item => item.UpdatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.Ignore(item => item.HasKey);
        // Uma só linha por instalação.
        builder.HasIndex(item => item.Key).IsUnique();
    }
}
