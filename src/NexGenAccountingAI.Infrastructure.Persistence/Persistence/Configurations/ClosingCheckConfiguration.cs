using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class ClosingCheckConfiguration : IEntityTypeConfiguration<ClosingCheck>
{
    public void Configure(EntityTypeBuilder<ClosingCheck> builder)
    {
        builder.ToTable("ClosingChecks", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.PeriodKey).HasMaxLength(ClosingCheck.PeriodKeyMaxLength).IsRequired();
        builder.Property(item => item.ItemKey).HasMaxLength(ClosingCheck.ItemKeyMaxLength).IsRequired();
        builder.Property(item => item.Note).HasMaxLength(ClosingCheck.NoteMaxLength);
        builder.HasIndex(item => new { item.CompanyId, item.PeriodKey, item.ItemKey }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
