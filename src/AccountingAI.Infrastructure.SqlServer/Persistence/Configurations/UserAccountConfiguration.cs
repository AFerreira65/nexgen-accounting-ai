using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts", "identity");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Email).HasMaxLength(254).IsRequired();
        builder.Property(item => item.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.NormalizedEmail).IsUnique();
    }
}
