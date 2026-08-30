using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer.Persistence;

public sealed class AccountingDbContext(DbContextOptions<AccountingDbContext> options)
    : DbContext(options)
{
    public DbSet<AccountingFirm> AccountingFirms => Set<AccountingFirm>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();

    public DbSet<UserCompanySettings> UserCompanySettings => Set<UserCompanySettings>();

    internal DbSet<FoundationState> FoundationStates => Set<FoundationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new AccountingFirmConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new UserAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyUserConfiguration());
        modelBuilder.ApplyConfiguration(new UserCompanySettingsConfiguration());

        modelBuilder.Entity<FoundationState>(entity =>
        {
            entity.ToTable("FoundationState", "infrastructure");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.Property(item => item.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        });
    }
}

internal sealed class FoundationState
{
    public int Id { get; init; }

    public required string SchemaVersion { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
