using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Domain.Banking;
using NexGenAccountingAI.Domain.Billing;
using NexGenAccountingAI.Domain.Efatura;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Domain.Saft;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence;

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

    public DbSet<JournalEntryChange> JournalEntryChanges => Set<JournalEntryChange>();

    public DbSet<NexGenAccountingAI.Domain.Operations.BackgroundJobRun> BackgroundJobRuns => Set<NexGenAccountingAI.Domain.Operations.BackgroundJobRun>();

    public DbSet<NexGenAccountingAI.Domain.Operations.AtExchange> AtExchanges => Set<NexGenAccountingAI.Domain.Operations.AtExchange>();

    public DbSet<AccountingDocumentType> AccountingDocumentTypes => Set<AccountingDocumentType>();

    public DbSet<DocumentNumberSeries> DocumentNumberSeries => Set<DocumentNumberSeries>();

    public DbSet<SaftValidationRun> SaftValidationRuns => Set<SaftValidationRun>();

    public DbSet<EFaturaPurchase> EFaturaPurchases => Set<EFaturaPurchase>();

    public DbSet<SavedAnalysis> SavedAnalyses => Set<SavedAnalysis>();

    public DbSet<FinancialMap> FinancialMaps => Set<FinancialMap>();

    public DbSet<SavedListing> SavedListings => Set<SavedListing>();

    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();

    public DbSet<ManagementReport> ManagementReports => Set<ManagementReport>();

    public DbSet<CompanyAiSettings> CompanyAiSettings => Set<CompanyAiSettings>();

    public DbSet<VatReturnFiling> VatReturnFilings => Set<VatReturnFiling>();

    public DbSet<VatDeclarationInput> VatDeclarationInputs => Set<VatDeclarationInput>();

    public DbSet<CompanyAtCredentials> CompanyAtCredentials => Set<CompanyAtCredentials>();

    public DbSet<AccountVatClassification> AccountVatClassifications => Set<AccountVatClassification>();

    public DbSet<IrcEstimateFiling> IrcEstimateFilings => Set<IrcEstimateFiling>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<VatCode> VatCodes => Set<VatCode>();

    public DbSet<EntryTemplate> EntryTemplates => Set<EntryTemplate>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();

    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();

    public DbSet<IrcReturnValue> IrcReturnValues => Set<IrcReturnValue>();

    public DbSet<FiscalDossierItem> FiscalDossierItems => Set<FiscalDossierItem>();

    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();

    public DbSet<SupplierEntryRule> SupplierEntryRules => Set<SupplierEntryRule>();

    public DbSet<SalesEntryRule> SalesEntryRules => Set<SalesEntryRule>();

    public DbSet<SalesDocument> SalesDocuments => Set<SalesDocument>();

    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();

    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();

    public DbSet<BankEntryRule> BankEntryRules => Set<BankEntryRule>();

    public DbSet<BankReconciliation> BankReconciliations => Set<BankReconciliation>();

    public DbSet<DocumentAttachment> DocumentAttachments => Set<DocumentAttachment>();

    public DbSet<DocumentAttachmentContent> DocumentAttachmentContents => Set<DocumentAttachmentContent>();

    public DbSet<TaxObligationRecord> TaxObligationRecords => Set<TaxObligationRecord>();

    public DbSet<ProvisionMapInput> ProvisionMapInputs => Set<ProvisionMapInput>();

    internal DbSet<FoundationState> FoundationStates => Set<FoundationState>();

    /// <summary>Collation ICU sem distinção de maiúsculas (equivalente ao CI_AS do SQL Server).</summary>
    public const string CaseInsensitiveCollation = "case_insensitive";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasCollation(CaseInsensitiveCollation, locale: "und-u-ks-level2", provider: "icu", deterministic: false);

        modelBuilder.ApplyConfiguration(new AccountingFirmConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new UserAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyUserConfiguration());
        modelBuilder.ApplyConfiguration(new UserCompanySettingsConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerAccountConfiguration());
        modelBuilder.ApplyConfiguration(new AccountingJournalConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryLineConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryChangeConfiguration());
        modelBuilder.ApplyConfiguration(new BackgroundJobRunConfiguration());
        modelBuilder.ApplyConfiguration(new AtExchangeConfiguration());
        modelBuilder.ApplyConfiguration(new AccountingDocumentTypeConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentNumberSeriesConfiguration());
        modelBuilder.ApplyConfiguration(new SaftValidationRunConfiguration());
        modelBuilder.ApplyConfiguration(new EFaturaPurchaseConfiguration());
        modelBuilder.ApplyConfiguration(new SavedAnalysisConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialMapConfiguration());
        modelBuilder.ApplyConfiguration(new SavedListingConfiguration());
        modelBuilder.ApplyConfiguration(new ReportTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new ManagementReportConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyAiSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new VatReturnFilingConfiguration());
        modelBuilder.ApplyConfiguration(new VatDeclarationInputConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyAtCredentialsConfiguration());
        modelBuilder.ApplyConfiguration(new AccountVatClassificationConfiguration());
        modelBuilder.ApplyConfiguration(new IrcEstimateFilingConfiguration());
        modelBuilder.ApplyConfiguration(new PartyConfiguration());
        modelBuilder.ApplyConfiguration(new VatCodeConfiguration());
        modelBuilder.ApplyConfiguration(new EntryTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new EntryTemplateLineConfiguration());
        modelBuilder.ApplyConfiguration(new CostCenterConfiguration());
        modelBuilder.ApplyConfiguration(new FixedAssetConfiguration());
        modelBuilder.ApplyConfiguration(new IrcReturnValueConfiguration());
        modelBuilder.ApplyConfiguration(new FiscalDossierItemConfiguration());
        modelBuilder.ApplyConfiguration(new BudgetLineConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierEntryRuleConfiguration());
        modelBuilder.ApplyConfiguration(new SalesEntryRuleConfiguration());
        modelBuilder.ApplyConfiguration(new SalesDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementImportConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementLineConfiguration());
        modelBuilder.ApplyConfiguration(new BankEntryRuleConfiguration());
        modelBuilder.ApplyConfiguration(new BankReconciliationConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentAttachmentContentConfiguration());
        modelBuilder.ApplyConfiguration(new TaxObligationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ProvisionMapInputConfiguration());

        AuditShadowProperties.Apply(modelBuilder);

        modelBuilder.Entity<FoundationState>(entity =>
        {
            entity.ToTable("FoundationState", "infrastructure");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.Property(item => item.CreatedAtUtc).HasDefaultValueSql("now()");
        });
    }
}

internal sealed class FoundationState
{
    public int Id { get; init; }

    public required string SchemaVersion { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
