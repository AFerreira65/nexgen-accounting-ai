using NexGenAccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class JournalEntryChangeConfiguration : IEntityTypeConfiguration<JournalEntryChange>
{
    public void Configure(EntityTypeBuilder<JournalEntryChange> builder)
    {
        builder.ToTable("JournalEntryChanges", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Reference).HasMaxLength(48).IsRequired();
        builder.Property(item => item.Action).HasConversion<int>();
        builder.Property(item => item.BeforeJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(item => new { item.CompanyId, item.Reference, item.ChangedAtUtc });
        builder.HasIndex(item => new { item.CompanyId, item.ChangedAtUtc });
    }
}
