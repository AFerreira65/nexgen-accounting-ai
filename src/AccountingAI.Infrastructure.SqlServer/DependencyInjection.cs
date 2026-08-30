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

        services.AddDbContext<AccountingDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure();
                    sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure");
                }));

        services.AddScoped<CompanyAccountingProvisioner>();
        services.AddScoped<AccountingMasterDataService>();
        services.AddScoped<TaxonomyReviewService>();
        services.AddScoped<SaftInspectionService>();
        services.AddScoped<SaftCompanyCreationService>();
        services.AddScoped<OrganizationAdminService>();
        services.AddScoped<JournalPostingService>();

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
