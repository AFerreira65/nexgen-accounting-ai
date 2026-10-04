using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class VatCodeConfiguration : IEntityTypeConfiguration<VatCode>
{
    public void Configure(EntityTypeBuilder<VatCode> builder)
    {
        builder.ToTable("VatCodes", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(VatCode.CodeMaxLength).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(VatCode.NameMaxLength).IsRequired();
        builder.Property(item => item.Rate).HasConversion<int>();
        builder.Property(item => item.Percentage).HasPrecision(5, 2);
        builder.Property(item => item.Nature).HasConversion<int>();
        builder.Property(item => item.Region).HasConversion<int>();
        builder.Property(item => item.IsActive).HasDefaultValue(true);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
