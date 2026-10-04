using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class FiscalDossierItemConfiguration : IEntityTypeConfiguration<FiscalDossierItem>
{
    public void Configure(EntityTypeBuilder<FiscalDossierItem> builder)
    {
        builder.ToTable("FiscalDossierItems", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Status).HasConversion<int>();
        builder.Property(item => item.Note).HasMaxLength(FiscalDossierItem.NoteMaxLength);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Year, item.Element }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
