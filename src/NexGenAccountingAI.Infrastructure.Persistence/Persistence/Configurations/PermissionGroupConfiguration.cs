using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class PermissionGroupConfiguration : IEntityTypeConfiguration<PermissionGroup>
{
    public void Configure(EntityTypeBuilder<PermissionGroup> builder)
    {
        builder.ToTable("PermissionGroups", "identity");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(80).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(300);
        builder.Property(item => item.SystemKey).HasMaxLength(32);
        builder.Property(item => item.Permissions).HasColumnType("text[]");
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.FirmId, item.Name }).IsUnique();
        builder.HasOne(item => item.Firm)
            .WithMany()
            .HasForeignKey(item => item.FirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
