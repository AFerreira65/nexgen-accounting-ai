using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class ClientPortalAccountConfiguration : IEntityTypeConfiguration<ClientPortalAccount>
{
    public void Configure(EntityTypeBuilder<ClientPortalAccount> builder)
    {
        builder.ToTable("ClientPortalAccounts", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Email).HasMaxLength(254).IsRequired();
        builder.Property(item => item.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(item => item.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Status).HasConversion<int>();
        builder.Property(item => item.PasswordHash).HasMaxLength(500);
        builder.Property(item => item.InviteTokenHash).HasMaxLength(100);
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.NormalizedEmail).IsUnique();
        builder.HasIndex(item => item.InviteTokenHash).IsUnique().HasFilter("\"InviteTokenHash\" IS NOT NULL");
        builder.HasMany(item => item.Companies).WithOne(item => item.Account).HasForeignKey(item => item.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClientPortalAccessConfiguration : IEntityTypeConfiguration<ClientPortalAccess>
{
    public void Configure(EntityTypeBuilder<ClientPortalAccess> builder)
    {
        builder.ToTable("ClientPortalAccess", "organization");
        builder.HasKey(item => new { item.AccountId, item.CompanyId });
        builder.HasIndex(item => item.CompanyId);
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PortalItemConfiguration : IEntityTypeConfiguration<PortalItem>
{
    public void Configure(EntityTypeBuilder<PortalItem> builder)
    {
        builder.ToTable("PortalItems", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Category).HasConversion<int>();
        builder.Property(item => item.Title).HasMaxLength(200).IsRequired();
        builder.Property(item => item.FileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.ContentType).HasMaxLength(200).IsRequired();
        builder.HasIndex(item => new { item.CompanyId, item.PublishedAtUtc });
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DocumentAttachment>().WithMany().HasForeignKey(item => item.AttachmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClientPortalUploadConfiguration : IEntityTypeConfiguration<ClientPortalUpload>
{
    public void Configure(EntityTypeBuilder<ClientPortalUpload> builder)
    {
        builder.ToTable("ClientPortalUploads", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.FileName).HasMaxLength(260).IsRequired();
        builder.HasIndex(item => new { item.CompanyId, item.AccountId, item.UploadedAtUtc });
        builder.HasOne<ClientPortalAccount>().WithMany().HasForeignKey(item => item.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DocumentAttachment>().WithMany().HasForeignKey(item => item.AttachmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
