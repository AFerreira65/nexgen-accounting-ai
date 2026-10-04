using NexGenAccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class SavedAnalysisConfiguration : IEntityTypeConfiguration<SavedAnalysis>
{
    public void Configure(EntityTypeBuilder<SavedAnalysis> builder)
    {
        builder.ToTable("SavedAnalysis", "reporting");
        builder.HasKey(item => item.Id);
        // O nome é único por empresa sem distinguir maiúsculas («Vendas» = «vendas»).
        builder.Property(item => item.Name).HasMaxLength(SavedAnalysis.NameMaxLength).IsRequired()
            .UseCollation(AccountingDbContext.CaseInsensitiveCollation);
        builder.Property(item => item.Description).HasMaxLength(SavedAnalysis.DescriptionMaxLength);
        builder.Property(item => item.DefinitionJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Name }).IsUnique();
    }
}
