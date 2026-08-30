using AccountingAI.Api;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
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

if (app.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("Database:SeedDemoData"))
{
    await app.Services.SeedDevelopmentOrganizationAsync().ConfigureAwait(false);
    await app.Services.SeedDevelopmentAccountingAsync().ConfigureAwait(false);
    await using var accountingScope = app.Services.CreateAsyncScope();
    var provisioner = accountingScope.ServiceProvider.GetRequiredService<CompanyAccountingProvisioner>();
    await provisioner.ProvisionAsync(DevelopmentDataSeeder.DemoCompanyId).ConfigureAwait(false);
    await provisioner.ProvisionAsync(DevelopmentDataSeeder.DemoIslandCompanyId).ConfigureAwait(false);
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
    phase = "F1-04B",
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

    app.MapGet("/api/v1/development/work-context/{userId:guid}", async (
        Guid userId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        var user = await context.UserAccounts
            .AsNoTracking()
            .Include(item => item.CompanyMemberships)
                .ThenInclude(membership => membership.Company)
            .Include(item => item.CompanyMemberships)
                .ThenInclude(membership => membership.Settings)
            .SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            User = new { user.Id, user.DisplayName, user.Email },
            Companies = user.CompanyMemberships
                .Where(item => item.IsActive && item.Company.IsActive)
                .OrderBy(item => item.Company.LegalName)
                .Select(item => new
                {
                    item.CompanyId,
                    item.Company.LegalName,
                    item.Company.TaxRegistrationNumber,
                    Region = item.Company.TaxRegion.ToString(),
                    item.Company.BaseCurrency,
                    AccountingStandard = item.Company.AccountingStandard.ToString(),
                    item.Company.AccountingSetupVersion,
                    Role = item.Role.ToString(),
                    Settings = ToSettingsResponse(item.Settings)
                })
        });
    });

    app.MapPut("/api/v1/development/work-context/{userId:guid}/companies/{companyId:guid}/settings", async (
        Guid userId,
        Guid companyId,
        UpdateUserCompanySettingsRequest request,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        var membership = await context.CompanyUsers
            .Include(item => item.Company)
            .Include(item => item.Settings)
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.CompanyId == companyId,
                cancellationToken)
            .ConfigureAwait(false);

        if (membership is null || !membership.IsActive || !membership.Company.IsActive)
        {
            return Results.NotFound();
        }

        if (!Enum.TryParse<InterfaceTheme>(request.Theme, true, out var theme))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Theme)] = ["O tema deve ser System, Light ou Dark."]
            });
        }

        try
        {
            var settings = membership.Settings ?? new UserCompanySettings(companyId, userId);
            settings.Update(
                request.Culture,
                request.TimeZone,
                request.DefaultFiscalYear,
                theme,
                request.CompactMode,
                request.PageSize);

            if (membership.Settings is null)
            {
                context.UserCompanySettings.Add(settings);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToSettingsResponse(settings));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings"] = [exception.Message]
            });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        var hasActiveAccess = await context.CompanyUsers
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId &&
                    item.CompanyId == companyId &&
                    item.IsActive &&
                    item.Company.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (!hasActiveAccess)
        {
            return Results.NotFound();
        }

        var entryEntities = await context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Include(item => item.Journal)
            .Include(item => item.Lines)
                .ThenInclude(item => item.Account)
            .OrderByDescending(item => item.EntryDate)
            .ThenByDescending(item => item.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var movements = entryEntities.Select(item => new
        {
            Id = item.Reference,
            Date = item.EntryDate,
            JournalId = item.Journal.Code,
            JournalName = item.Journal.Name,
            Document = item.DocumentReference,
            item.Description,
            Status = item.Status == JournalEntryStatus.Reviewed ? "Conferido" : "Por conferir",
            Amount = item.TotalAmount,
            Lines = item.Lines
                .OrderBy(line => line.Sequence)
                .Select(line => new
                {
                    Account = line.Account.Code,
                    AccountName = line.Account.Name,
                    line.Description,
                    line.Debit,
                    line.Credit
                })
        });

        return Results.Ok(movements);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/setup", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        var company = await context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == companyId &&
                item.IsActive &&
                item.UserMemberships.Any(membership =>
                    membership.UserId == userId && membership.IsActive), cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return Results.NotFound();
        }

        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Code)
            .Select(item => new
            {
                item.Code,
                item.Name,
                item.ParentCode,
                item.AllowsPosting,
                item.TaxonomyReference,
                item.TaxonomyCode,
                item.IsActive,
                item.SourceTemplateVersion
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var journals = await context.Journals
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Code)
            .Select(item => new { item.Code, item.Name, item.IsActive, item.SourceTemplateVersion })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var documentTypes = await context.AccountingDocumentTypes
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Code)
            .Select(item => new
            {
                item.Code,
                item.Name,
                DefaultJournalCode = item.DefaultJournal.Code,
                item.IsActive,
                item.SourceTemplateVersion
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var template = AccountingTemplateCatalog.For(company.AccountingStandard);
        return Results.Ok(new
        {
            Standard = company.AccountingStandard.ToString(),
            Template = new
            {
                template.Code,
                template.Name,
                company.AccountingSetupVersion,
                template.LegalSource,
                template.TaxonomyReference,
                template.TaxonomyName,
                template.TaxonomyLegalSource
            },
            Accounts = accounts,
            Journals = journals,
            DocumentTypes = documentTypes
        });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/taxonomy-review", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        TaxonomyReviewService reviewService,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var review = await reviewService.BuildAsync(companyId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            review.Reference,
            review.TaxonomyName,
            review.TaxonomyLegalSource,
            review.PostingAccountCount,
            review.ClassifiedCount,
            Accounts = review.Accounts.Select(item => new
            {
                item.Code,
                item.Name,
                item.TaxonomyReference,
                item.TaxonomyCode,
                item.SuggestedCode,
                Status = item.Status.ToString()
            })
        });
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/accounts", async (
        Guid userId,
        Guid companyId,
        CreateAccountRequest request,
        AccountingDbContext context,
        AccountingMasterDataService masterData,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            var account = await masterData
                .CreateAccountAsync(
                    companyId,
                    request.Code,
                    request.Name,
                    request.ParentCode,
                    request.AllowsPosting,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(account);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["account"] = [exception.Message]
            });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/accounts/{code}", async (
        Guid userId,
        Guid companyId,
        string code,
        UpdateAccountRequest request,
        AccountingDbContext context,
        AccountingMasterDataService masterData,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            var account = await masterData
                .UpdateAccountAsync(
                    companyId,
                    code,
                    request.Name,
                    request.IsActive,
                    request.TaxonomyReference,
                    request.TaxonomyCode,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(account);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["account"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/accounts/apply-taxonomy-suggestions", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        AccountingMasterDataService masterData,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var applied = await masterData
            .ApplyTemplateTaxonomySuggestionsAsync(companyId, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new { applied });
    });
}

await app.RunAsync().ConfigureAwait(false);

public partial class Program
{
    private static Task<bool> HasCompanyAccessAsync(
        AccountingDbContext context,
        Guid userId,
        Guid companyId,
        CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(
                        membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);

    private static object? ToSettingsResponse(UserCompanySettings? settings) => settings is null
        ? null
        : new
        {
            settings.Culture,
            settings.TimeZone,
            settings.DefaultFiscalYear,
            Theme = settings.Theme.ToString(),
            settings.CompactMode,
            settings.PageSize,
            settings.UpdatedAtUtc
        };
}

public sealed record UpdateUserCompanySettingsRequest(
    string Culture,
    string TimeZone,
    int? DefaultFiscalYear,
    string Theme,
    bool CompactMode,
    int PageSize);

public sealed record CreateAccountRequest(
    string Code,
    string Name,
    string? ParentCode,
    bool AllowsPosting);

public sealed record UpdateAccountRequest(
    string Name,
    bool IsActive,
    string? TaxonomyReference,
    int? TaxonomyCode);
