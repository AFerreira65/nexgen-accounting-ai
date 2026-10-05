using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountingPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Accounting");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string 'ConnectionStrings:Accounting' is required.");
        }

        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<AuditStampingInterceptor>();
        services.AddScoped<JournalEntryHistoryInterceptor>();

        services.AddDbContext<AccountingDbContext>((serviceProvider, options) =>
            options
                // O histórico primeiro: lê os valores de antes; o carimbo de auditoria vem depois.
                .AddInterceptors(
                    serviceProvider.GetRequiredService<JournalEntryHistoryInterceptor>(),
                    serviceProvider.GetRequiredService<AuditStampingInterceptor>())
                .UseNpgsql(
                connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure();
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure");
                    // O valor por omissão (30s) chega para uma empresa normal, mas uma leitura
                    // ampla (ex.: lista de movimentos) numa empresa com um histórico muito grande
                    // — importado de um SAF-T com anos de linhas por liquidar, por exemplo — pode
                    // ultrapassar 30s na primeira consulta a seguir à importação.
                    npgsqlOptions.CommandTimeout(120);
                }));

        services.AddScoped<CompanyAccountingProvisioner>();
        services.AddScoped<AccountingMasterDataService>();
        services.AddScoped<TaxonomyReviewService>();
        services.AddScoped<SaftInspectionService>();
        services.AddScoped<SaftCompanyCreationService>();
        services.AddScoped<SaftAccountsPreviewService>();
        services.AddScoped<SaftAccountsImportService>();
        services.AddScoped<SaftOpeningBalancesImportService>();
        services.AddScoped<JournalEntryHistoryService>();
        services.AddScoped<SaftEntriesPreviewService>();
        services.AddScoped<SaftEntriesImportService>();
        services.AddScoped<EFaturaPurchaseImportService>();
        services.AddScoped<NexGenAccountingAI.Application.CrossCheck.ICrossCheckService, CrossCheckService>();
        services.AddScoped<RecordInfoService>();
        services.AddScoped<DataDictionaryService>();
        services.AddScoped<SavedAnalysisService>();
        services.AddScoped<NexGenAccountingAI.Application.Reporting.IAnalysisRunner, AnalysisRunner>();
        services.AddScoped<ReportTemplateService>();
        services.AddScoped<NexGenAccountingAI.Application.ManagementReports.IManagementReportDataService, ManagementReportDataService>();
        services.AddScoped<CompanyAiSettingsService>();
        services.AddScoped<ManagementReportService>();
        services.AddScoped<NexGenAccountingAI.Application.Vat.IVatReturnService, VatReturnService>();
        services.AddScoped<VatReturnFilingService>();
        services.AddScoped<CompanyAtCredentialsService>();
        services.AddScoped<AccountVatClassificationService>();
        services.AddScoped<PartyService>();
        services.AddScoped<VatCodeService>();
        services.AddScoped<EntryTemplateService>();
        services.AddScoped<CostCenterService>();
        services.AddScoped<FixedAssetService>();
        services.AddScoped<SaftExportService>();
        services.AddScoped<FinancialStatementsService>();
        services.AddScoped<Modelo22Service>();
        services.AddScoped<IesAnnexAService>();
        services.AddScoped<WithholdingService>();
        services.AddScoped<PartyAgingService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<SuggestedEntryService>();
        services.AddScoped<SalesSuggestedEntryService>();
        services.AddScoped<BankStatementService>();
        services.AddScoped<BankEntryService>();
        services.AddScoped<DocumentArchiveService>();
        services.AddScoped<PurchaseClassificationService>();
        services.AddScoped<OwnerReportService>();
        services.AddScoped<TreasuryForecastService>();
        services.AddScoped<ObligationCalendarService>();
        services.AddScoped<FirmDashboardService>();
        services.AddScoped<DossierMapsService>();
        services.AddScoped<ProvisionMapService>();
        services.AddScoped<FiscalDossierService>();
        services.AddScoped<PartyImportService>();
        services.AddScoped<PartyLedgerService>();
        services.AddScoped<VatSummaryService>();
        services.AddScoped<VatDeclarationService>();
        services.AddScoped<VatSettlementEntryService>();
        services.AddScoped<RecapitulativeStatementService>();
        services.AddScoped<IrcEstimateService>();
        services.AddScoped<IrcEstimateFilingService>();
        services.AddScoped<OrganizationAdminService>();
        services.AddScoped<AuthService>();
        services.AddScoped<JournalPostingService>();
        services.AddScoped<DocumentNumberSeriesService>();
        services.AddScoped<AccountLedgerService>();
        services.AddScoped<JournalReportService>();
        services.AddScoped<YearEndClosingService>();
        services.AddScoped<YearOpeningService>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<AccountingDbContext>(
                name: "database",
                tags: ["ready"]);

        return services;
    }

    public static async Task MigrateAccountingDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
