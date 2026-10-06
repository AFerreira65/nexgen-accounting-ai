using NexGenAccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class SavedListingConfiguration : IEntityTypeConfiguration<SavedListing>
{
    public void Configure(EntityTypeBuilder<SavedListing> builder)
    {
        builder.ToTable("SavedListing", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(SavedListing.NameMaxLength).IsRequired()
            .UseCollation(AccountingDbContext.CaseInsensitiveCollation);
        builder.Property(item => item.Description).HasMaxLength(SavedListing.DescriptionMaxLength);
        builder.Property(item => item.Scope).HasConversion<int>();
        builder.Property(item => item.DefinitionJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        // O nome é único por empresa, dono e alcance, sem distinguir maiúsculas.
        builder.HasIndex(item => new { item.CompanyId, item.OwnerUserId, item.Scope, item.Name }).IsUnique();
        builder.HasIndex(item => item.FirmId);
    }
}
