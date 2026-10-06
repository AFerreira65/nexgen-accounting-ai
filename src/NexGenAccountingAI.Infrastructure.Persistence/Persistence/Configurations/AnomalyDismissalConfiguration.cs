using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AnomalyDismissalConfiguration : IEntityTypeConfiguration<AnomalyDismissal>
{
    public void Configure(EntityTypeBuilder<AnomalyDismissal> builder)
    {
        builder.ToTable("AnomalyDismissals", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.FindingKey).HasMaxLength(AnomalyDismissal.KeyMaxLength).IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(AnomalyDismissal.ReasonMaxLength);
        builder.HasIndex(item => new { item.CompanyId, item.FindingKey }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
