using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class CompanyUserConfiguration : IEntityTypeConfiguration<CompanyUser>
{
    public void Configure(EntityTypeBuilder<CompanyUser> builder)
    {
        builder.ToTable("CompanyUsers", "identity");
        builder.HasKey(item => new { item.CompanyId, item.UserId });
        builder.Property(item => item.Role).HasConversion<int>();
        builder.Property(item => item.IsResponsible).HasDefaultValue(false);
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.UserId);
        builder.HasOne(item => item.PermissionGroup)
            .WithMany()
            .HasForeignKey(item => item.PermissionGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Company)
            .WithMany(item => item.UserMemberships)
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.User)
            .WithMany(item => item.CompanyMemberships)
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
