using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Efatura;
using AccountingAI.Domain.Organizations;
using AccountingAI.Domain.Reporting;
using AccountingAI.Domain.Saft;
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

    public DbSet<DocumentNumberSeries> DocumentNumberSeries => Set<DocumentNumberSeries>();

    public DbSet<SaftValidationRun> SaftValidationRuns => Set<SaftValidationRun>();

    public DbSet<EFaturaPurchase> EFaturaPurchases => Set<EFaturaPurchase>();

    public DbSet<SavedAnalysis> SavedAnalyses => Set<SavedAnalysis>();

    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();

    public DbSet<ManagementReport> ManagementReports => Set<ManagementReport>();

    public DbSet<CompanyAiSettings> CompanyAiSettings => Set<CompanyAiSettings>();

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
        modelBuilder.ApplyConfiguration(new DocumentNumberSeriesConfiguration());
        modelBuilder.ApplyConfiguration(new SaftValidationRunConfiguration());
        modelBuilder.ApplyConfiguration(new EFaturaPurchaseConfiguration());
        modelBuilder.ApplyConfiguration(new SavedAnalysisConfiguration());
        modelBuilder.ApplyConfiguration(new ReportTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new ManagementReportConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyAiSettingsConfiguration());

        AuditShadowProperties.Apply(modelBuilder);

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
