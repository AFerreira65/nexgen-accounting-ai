using AccountingAI.Domain.Accounting;
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

    public DbSet<LedgerAccount> Accounts => Set<LedgerAccount>();

    public DbSet<AccountingJournal> Journals => Set<AccountingJournal>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();

    public DbSet<AccountingDocumentType> AccountingDocumentTypes => Set<AccountingDocumentType>();

    internal DbSet<FoundationState> FoundationStates => Set<FoundationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new AccountingFirmConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new UserAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyUserConfiguration());
        modelBuilder.ApplyConfiguration(new UserCompanySettingsConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerAccountConfiguration());
        modelBuilder.ApplyConfiguration(new AccountingJournalConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryLineConfiguration());
        modelBuilder.ApplyConfiguration(new AccountingDocumentTypeConfiguration());

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
