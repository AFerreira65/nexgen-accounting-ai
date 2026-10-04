using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class DocumentAttachmentConfiguration : IEntityTypeConfiguration<DocumentAttachment>
{
    public void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.ToTable("DocumentAttachments", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.FileName).HasMaxLength(DocumentAttachment.FileNameMaxLength).IsRequired();
        builder.Property(item => item.ContentType).HasMaxLength(60).IsRequired();
        builder.Property(item => item.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Notes).HasMaxLength(DocumentAttachment.NotesMaxLength);
        builder.Property(item => item.JournalEntryReference).HasMaxLength(DocumentAttachment.EntryReferenceMaxLength);
        builder.Property(item => item.QrText).HasMaxLength(InvoiceQrCode.MaxLength);
        builder.Property(item => item.QrIssuerTaxId).HasMaxLength(20);
        builder.Property(item => item.QrDocumentNo).HasMaxLength(60);
        builder.Property(item => item.QrGrossTotal).HasPrecision(18, 2);
        builder.Property(item => item.QrTaxTotal).HasPrecision(18, 2);
        builder.Property(item => item.UploadedAtUtc).HasDefaultValueSql("now()");
        builder.HasIndex(item => new { item.CompanyId, item.Sha256 }).IsUnique();
        builder.HasIndex(item => new { item.CompanyId, item.JournalEntryReference });
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DocumentAttachmentContentConfiguration : IEntityTypeConfiguration<DocumentAttachmentContent>
{
    public void Configure(EntityTypeBuilder<DocumentAttachmentContent> builder)
    {
        builder.ToTable("DocumentAttachmentContents", "accounting");
        builder.HasKey(item => item.AttachmentId);
        builder.Property(item => item.Content).IsRequired();
        builder.HasOne<DocumentAttachment>()
            .WithOne()
            .HasForeignKey<DocumentAttachmentContent>(item => item.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
