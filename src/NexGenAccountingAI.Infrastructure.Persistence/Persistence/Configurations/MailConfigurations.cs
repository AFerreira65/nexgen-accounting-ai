using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class MailAccountConfiguration : IEntityTypeConfiguration<MailAccount>
{
    public void Configure(EntityTypeBuilder<MailAccount> builder)
    {
        builder.ToTable("MailAccounts", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Purpose).HasConversion<int>();
        builder.Property(item => item.Provider).HasConversion<int>();
        builder.Property(item => item.SmtpSecurity).HasConversion<int>();
        builder.Property(item => item.ImapSecurity).HasConversion<int>();
        builder.Property(item => item.Address).HasMaxLength(MailAccount.AddressMaxLength).IsRequired();
        builder.Property(item => item.DisplayName).HasMaxLength(160);
        builder.Property(item => item.SmtpHost).HasMaxLength(255);
        builder.Property(item => item.ImapHost).HasMaxLength(255);
        builder.Property(item => item.ImapFolder).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Username).HasMaxLength(254);
        builder.Property(item => item.Secret).HasMaxLength(4000);
        builder.Property(item => item.AuthorizedAccount).HasMaxLength(254);
        builder.Property(item => item.LastError).HasMaxLength(500);
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.Ignore(item => item.HasSecret);
        builder.HasIndex(item => new { item.Purpose, item.OwnerId }).IsUnique();
    }
}

internal sealed class MailOAuthAppSettingsConfiguration : IEntityTypeConfiguration<MailOAuthAppSettings>
{
    public void Configure(EntityTypeBuilder<MailOAuthAppSettings> builder)
    {
        builder.ToTable("MailOAuthApps", "organization");
        builder.HasKey(item => item.Provider);
        builder.Property(item => item.Provider).HasConversion<int>().ValueGeneratedNever();
        builder.Property(item => item.ClientId).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ClientSecret).HasMaxLength(4000);
        builder.Property(item => item.Tenant).HasMaxLength(200);
        builder.Property(item => item.UpdatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.Ignore(item => item.IsConfigured);
    }
}

internal sealed class InboundEmailConfiguration : IEntityTypeConfiguration<InboundEmail>
{
    public void Configure(EntityTypeBuilder<InboundEmail> builder)
    {
        builder.ToTable("InboundEmails", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.MessageId).HasMaxLength(300).IsRequired();
        builder.Property(item => item.FromAddress).HasMaxLength(254).IsRequired();
        builder.Property(item => item.Subject).HasMaxLength(300).IsRequired();
        builder.Property(item => item.BodyPreview).HasMaxLength(500);
        builder.Property(item => item.Status).HasConversion<int>();
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.FirmId, item.MessageId }).IsUnique();
        builder.HasIndex(item => new { item.FirmId, item.Status });
        builder.HasOne<AccountingFirm>().WithMany().HasForeignKey(item => item.FirmId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Attachments).WithOne().HasForeignKey(item => item.EmailId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InboundEmailAttachmentConfiguration : IEntityTypeConfiguration<InboundEmailAttachment>
{
    public void Configure(EntityTypeBuilder<InboundEmailAttachment> builder)
    {
        builder.ToTable("InboundEmailAttachments", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.FileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.ContentType).HasMaxLength(200).IsRequired();
        builder.Ignore(item => item.IsArchived);
    }
}
