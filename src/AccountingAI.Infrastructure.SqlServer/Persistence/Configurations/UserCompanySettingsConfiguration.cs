using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class UserCompanySettingsConfiguration : IEntityTypeConfiguration<UserCompanySettings>
{
    public void Configure(EntityTypeBuilder<UserCompanySettings> builder)
    {
        builder.ToTable("UserCompanySettings", "identity");
        builder.HasKey(item => new { item.CompanyId, item.UserId });
        builder.Property(item => item.Culture).HasMaxLength(16).IsRequired();
        builder.Property(item => item.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Theme).HasConversion<int>();
        builder.Property(item => item.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasOne(item => item.CompanyUser)
            .WithOne(item => item.Settings)
            .HasForeignKey<UserCompanySettings>(item => new { item.CompanyId, item.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
