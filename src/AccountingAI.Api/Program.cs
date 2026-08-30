using AccountingAI.Api;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddProblemDetails();
builder.Services.AddAccountingSqlServer(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
var applyMigrations = migrateOnly || builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");

if (applyMigrations)
{
    await app.Services.MigrateAccountingDatabaseAsync().ConfigureAwait(false);
}

if (builder.Configuration.GetValue<bool>("Database:SeedDemoData"))
{
    await app.Services.SeedDevelopmentOrganizationAsync().ConfigureAwait(false);
}

if (migrateOnly)
{
    app.Logger.LogInformation("Database migrations applied successfully.");
    return;
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.MapGet("/api/v1/foundation", () => Results.Ok(new
{
    service = "AccountingAI.Api",
    phase = "F1-02C",
    status = "ready"
}));

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/v1/development/organization", async (
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        var firmEntities = await context.AccountingFirms
            .AsNoTracking()
            .Include(item => item.Companies)
                .ThenInclude(company => company.UserMemberships)
                    .ThenInclude(membership => membership.User)
            .Include(item => item.Companies)
                .ThenInclude(company => company.UserMemberships)
                    .ThenInclude(membership => membership.Settings)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var firms = firmEntities.Select(item => new
            {
                item.Id,
                item.Name,
                item.Slug,
                Companies = item.Companies
                    .OrderBy(company => company.LegalName)
                    .Select(company => new
                    {
                        company.Id,
                        company.LegalName,
                        company.TaxRegistrationNumber,
                        Region = company.TaxRegion.ToString(),
                        company.BaseCurrency,
                        company.IsActive,
                        Users = company.UserMemberships
                            .OrderBy(membership => membership.User.DisplayName)
                            .Select(membership => new
                            {
                                membership.UserId,
                                membership.User.DisplayName,
                                membership.User.Email,
                                Role = membership.Role.ToString(),
                                membership.IsActive,
                                Settings = membership.Settings is null
                                    ? null
                                    : new
                                    {
                                        membership.Settings.Culture,
                                        membership.Settings.TimeZone,
                                        membership.Settings.DefaultFiscalYear,
                                        Theme = membership.Settings.Theme.ToString(),
                                        membership.Settings.CompactMode,
                                        membership.Settings.PageSize
                                    }
                            })
                    })
            })
            .ToList();

        return Results.Ok(firms);
    });
}

await app.RunAsync().ConfigureAwait(false);

public partial class Program
{
}
