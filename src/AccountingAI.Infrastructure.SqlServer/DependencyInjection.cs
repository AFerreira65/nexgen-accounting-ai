using AccountingAI.Application.Auditing;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.Infrastructure.SqlServer;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountingSqlServer(
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

        services.AddDbContext<AccountingDbContext>((serviceProvider, options) =>
            options
                .AddInterceptors(serviceProvider.GetRequiredService<AuditStampingInterceptor>())
                .UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure();
                    sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure");
                    // O valor por omissão do SqlClient (30s) chega para uma empresa normal, mas
                    // uma leitura ampla (ex.: lista de movimentos) numa empresa com um histórico
                    // muito grande — importado de um SAF-T com anos de linhas por liquidar, por
                    // exemplo — pode ainda não ter as páginas em cache na primeira consulta a
                    // seguir à importação, e ultrapassar 30s só por causa disso.
                    sqlOptions.CommandTimeout(120);
                }));

        services.AddScoped<CompanyAccountingProvisioner>();
        services.AddScoped<AccountingMasterDataService>();
        services.AddScoped<TaxonomyReviewService>();
        services.AddScoped<SaftInspectionService>();
        services.AddScoped<SaftCompanyCreationService>();
        services.AddScoped<SaftAccountsPreviewService>();
        services.AddScoped<SaftAccountsImportService>();
        services.AddScoped<SaftOpeningBalancesImportService>();
        services.AddScoped<SaftEntriesPreviewService>();
        services.AddScoped<SaftEntriesImportService>();
        services.AddScoped<EFaturaPurchaseImportService>();
        services.AddScoped<AccountingAI.Application.CrossCheck.ICrossCheckService, CrossCheckService>();
        services.AddScoped<RecordInfoService>();
        services.AddScoped<DataDictionaryService>();
        services.AddScoped<SavedAnalysisService>();
        services.AddScoped<AccountingAI.Application.Reporting.IAnalysisRunner, AnalysisRunner>();
        services.AddScoped<ReportTemplateService>();
        services.AddScoped<AccountingAI.Application.ManagementReports.IManagementReportDataService, ManagementReportDataService>();
        services.AddScoped<CompanyAiSettingsService>();
        services.AddScoped<ManagementReportService>();
        services.AddScoped<AccountingAI.Application.Vat.IVatReturnService, VatReturnService>();
        services.AddScoped<VatReturnFilingService>();
        services.AddScoped<CompanyAtCredentialsService>();
        services.AddScoped<AccountVatClassificationService>();
        services.AddScoped<VatSummaryService>();
        services.AddScoped<IrcEstimateService>();
        services.AddScoped<IrcEstimateFilingService>();
        services.AddScoped<OrganizationAdminService>();
        services.AddScoped<JournalPostingService>();
        services.AddScoped<DocumentNumberSeriesService>();
        services.AddScoped<AccountLedgerService>();
        services.AddScoped<JournalReportService>();
        services.AddScoped<YearEndClosingService>();
        services.AddScoped<YearOpeningService>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<AccountingDbContext>(
                name: "sql-server",
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
