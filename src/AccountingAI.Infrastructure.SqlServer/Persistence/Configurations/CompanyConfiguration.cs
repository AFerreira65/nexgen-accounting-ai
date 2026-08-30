using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.TaxRegistrationNumber).HasMaxLength(32).IsRequired();
        builder.Property(item => item.TaxRegion).HasConversion<int>();
        builder.Property(item => item.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.AccountingFirmId, item.TaxRegistrationNumber }).IsUnique();
        builder.HasOne(item => item.AccountingFirm)
            .WithMany(item => item.Companies)
            .HasForeignKey(item => item.AccountingFirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
