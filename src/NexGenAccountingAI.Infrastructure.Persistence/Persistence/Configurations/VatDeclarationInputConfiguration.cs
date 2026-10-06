using NexGenAccountingAI.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class VatDeclarationInputConfiguration : IEntityTypeConfiguration<VatDeclarationInput>
{
    public void Configure(EntityTypeBuilder<VatDeclarationInput> builder)
    {
        builder.ToTable("VatDeclarationInputs", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.InputsJson).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.PeriodFrom, item.PeriodTo }).IsUnique();
    }
}
