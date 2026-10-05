using NexGenAccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class FinancialMapConfiguration : IEntityTypeConfiguration<FinancialMap>
{
    public void Configure(EntityTypeBuilder<FinancialMap> builder)
    {
        builder.ToTable("FinancialMap", "reporting");
        builder.HasKey(item => item.Id);
        // O nome é único por empresa sem distinguir maiúsculas («Vendas» = «vendas»).
        builder.Property(item => item.Name).HasMaxLength(FinancialMap.NameMaxLength).IsRequired()
            .UseCollation(AccountingDbContext.CaseInsensitiveCollation);
        builder.Property(item => item.Description).HasMaxLength(FinancialMap.DescriptionMaxLength);
        builder.Property(item => item.DefinitionJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Name }).IsUnique();
    }
}
