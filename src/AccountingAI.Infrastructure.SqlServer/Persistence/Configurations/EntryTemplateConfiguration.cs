using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class EntryTemplateConfiguration : IEntityTypeConfiguration<EntryTemplate>
{
    public void Configure(EntityTypeBuilder<EntryTemplate> builder)
    {
        builder.ToTable("EntryTemplates", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(EntryTemplate.CodeMaxLength).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(EntryTemplate.NameMaxLength).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(EntryTemplate.DescriptionMaxLength);
        builder.Property(item => item.IsActive).HasDefaultValue(true);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountingJournal>()
            .WithMany()
            .HasForeignKey(item => item.JournalId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountingDocumentType>()
            .WithMany()
            .HasForeignKey(item => item.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Lines)
            .WithOne()
            .HasForeignKey(line => line.EntryTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(item => item.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class EntryTemplateLineConfiguration : IEntityTypeConfiguration<EntryTemplateLine>
{
    public void Configure(EntityTypeBuilder<EntryTemplateLine> builder)
    {
        builder.ToTable("EntryTemplateLines", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Kind).HasConversion<int>();
        builder.Property(item => item.Side).HasConversion<int>();
        builder.Property(item => item.Description).HasMaxLength(EntryTemplateLine.DescriptionMaxLength);
        builder.HasIndex(item => new { item.EntryTemplateId, item.Sequence }).IsUnique();
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(item => item.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VatCode>()
            .WithMany()
            .HasForeignKey(item => item.VatCodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
