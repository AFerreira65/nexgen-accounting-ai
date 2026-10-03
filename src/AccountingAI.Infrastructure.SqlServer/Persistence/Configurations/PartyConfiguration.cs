using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("Parties", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(item => item.TaxRegistrationNumber)
            .HasMaxLength(Party.TaxRegistrationNumberMaxLength)
            .IsRequired();
        builder.Property(item => item.Name).HasMaxLength(Party.NameMaxLength).IsRequired();
        builder.Property(item => item.IsActive).HasDefaultValue(true);
        builder.Property(item => item.AddressDetail).HasMaxLength(210);
        builder.Property(item => item.City).HasMaxLength(50);
        builder.Property(item => item.PostalCode).HasMaxLength(20);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.CountryCode, item.TaxRegistrationNumber }).IsUnique();
        builder.HasIndex(item => new { item.CompanyId, item.Name });
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(item => item.CustomerAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(item => item.SupplierAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
