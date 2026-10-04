using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AccountingFirmConfiguration : IEntityTypeConfiguration<AccountingFirm>
{
    public void Configure(EntityTypeBuilder<AccountingFirm> builder)
    {
        builder.ToTable("AccountingFirms", "organization");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Slug).HasMaxLength(100).IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.Slug).IsUnique();
    }
}
