using AccountingAI.Api;
using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Files;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddProblemDetails();
builder.Services.AddAccountingSqlServer(builder.Configuration);
builder.Services.AddSaftFileReading();

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
    phase = "F1-39",
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
                        company.PostingLockDate,
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
        CancellationToken cancellationToken,
        string? documentType) =>
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

        var query = context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Include(item => item.Journal)
            .Include(item => item.DocumentType)
            .Include(item => item.Lines)
                .ThenInclude(item => item.Account)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            var normalizedDocumentType = documentType.Trim().ToUpperInvariant();
            query = query.Where(item => item.DocumentType != null && item.DocumentType.Code == normalizedDocumentType);
        }

        var entryEntities = await query
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
            DocumentType = item.DocumentType == null ? null : item.DocumentType.Code,
            DocumentTypeName = item.DocumentType == null ? null : item.DocumentType.Name,
            Document = item.DocumentReference,
            item.Description,
            item.Notes,
            Status = item.Status switch
            {
                JournalEntryStatus.Reviewed => "Conferido",
                JournalEntryStatus.Voided => "Anulado",
                _ => "Por conferir"
            },
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

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements", async (
        Guid userId,
        Guid companyId,
        CreateMovementRequest request,
        AccountingDbContext context,
        JournalPostingService posting,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            var lines = (request.Lines ?? [])
                .Select(line => new JournalEntryLineInput(
                    line.AccountCode,
                    line.Description,
                    line.Debit,
                    line.Credit))
                .ToList();

            var movement = await posting
                .PostAsync(
                    companyId,
                    request.JournalCode,
                    request.EntryDate,
                    request.DocumentReference,
                    request.Description,
                    request.Reviewed,
                    lines,
                    cancellationToken,
                    request.DocumentTypeCode,
                    request.Notes)
                .ConfigureAwait(false);

            return Results.Ok(ToMovementResponse(movement));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["movement"] = [exception.Message]
            });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements/{reference}", async (
        Guid userId,
        Guid companyId,
        string reference,
        CreateMovementRequest request,
        AccountingDbContext context,
        JournalPostingService posting,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            var lines = (request.Lines ?? [])
                .Select(line => new JournalEntryLineInput(line.AccountCode, line.Description, line.Debit, line.Credit))
                .ToList();
            var movement = await posting
                .ReviseAsync(companyId, reference, request.JournalCode, request.EntryDate,
                    request.DocumentReference, request.Description, request.Reviewed, lines, cancellationToken,
                    request.DocumentTypeCode, request.Notes)
                .ConfigureAwait(false);
            return Results.Ok(ToMovementResponse(movement));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["movement"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements/{reference}/review", async (
        Guid userId,
        Guid companyId,
        string reference,
        AccountingDbContext context,
        JournalPostingService posting,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            var movement = await posting.MarkReviewedAsync(companyId, reference, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToMovementResponse(movement));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["movement"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements/{reference}/void", async (
        Guid userId,
        Guid companyId,
        string reference,
        AccountingDbContext context,
        JournalPostingService posting,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            var movement = await posting.VoidAsync(companyId, reference, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToMovementResponse(movement));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["movement"] = [exception.Message] });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/number-series", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        DocumentNumberSeriesService numberSeries,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(await numberSeries.ListAsync(companyId, year, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/number-series/{series}/{year:int}", async (
        Guid userId,
        Guid companyId,
        string series,
        int year,
        UpdateDocumentNumberSeriesRequest request,
        AccountingDbContext context,
        DocumentNumberSeriesService numberSeries,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(await numberSeries
                .AdvanceAsync(companyId, series, year, request.NextNumber, request.Width, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["numberSeries"] = [exception.Message] });
        }
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

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/trial-balance", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        AccountLedgerService ledger,
        CancellationToken cancellationToken,
        string? from,
        string? to) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var balance = await ledger.TrialBalanceAsync(companyId, fromDate, toDate, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new
        {
            balance.TotalOpeningBalance,
            balance.TotalDebit,
            balance.TotalCredit,
            Rows = balance.Rows.Select(row => new
            {
                row.AccountCode,
                row.AccountName,
                row.OpeningBalance,
                row.Debit,
                row.Credit,
                row.Balance
            })
        });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/journal-report", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        JournalReportService reportService,
        CancellationToken cancellationToken,
        string? journal,
        string? documentType,
        int? year,
        string? from,
        string? to) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var report = await reportService
            .BuildAsync(companyId, journal, documentType, year, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            report.TotalDebit,
            report.TotalCredit,
            Entries = report.Entries.Select(entry => new
            {
                Id = entry.Reference,
                Date = entry.EntryDate,
                JournalId = entry.JournalCode,
                DocumentType = entry.DocumentType,
                Document = entry.DocumentReference,
                entry.Description,
                entry.Status,
                Amount = entry.Amount,
                Lines = entry.Lines.Select(line => new
                {
                    Account = line.AccountCode,
                    line.AccountName,
                    line.Description,
                    line.Debit,
                    line.Credit
                })
            })
        });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/accounts/{code}/ledger", async (
        Guid userId,
        Guid companyId,
        string code,
        AccountingDbContext context,
        AccountLedgerService ledger,
        CancellationToken cancellationToken,
        string? from,
        string? to) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var result = await ledger.AccountLedgerAsync(companyId, code, fromDate, toDate, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            result.AccountCode,
            result.AccountName,
            result.OpeningBalance,
            result.Balance,
            Entries = result.Entries.Select(entry => new
            {
                entry.Reference,
                entry.EntryDate,
                entry.JournalCode,
                entry.Description,
                entry.Notes,
                entry.Debit,
                entry.Credit,
                entry.RunningBalance
            })
        });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/year-end-closing", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        YearEndClosingService closing,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(await closing.PreviewAsync(companyId, year, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = [exception.Message] });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/closed-years", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        YearEndClosingService closing,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        return Results.Ok(await closing.ClosedYearsAsync(companyId, cancellationToken).ConfigureAwait(false));
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/posting-lock-date", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var lockDate = await context.Companies.AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.PostingLockDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new { lockDate });
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/year-end-closing", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        YearEndClosingService closing,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(ToMovementResponse(await closing.RegisterAsync(companyId, year, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["yearEndClosing"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/year-end-closing/reopen", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        YearEndClosingService closing,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(ToMovementResponse(await closing.ReopenAsync(companyId, year, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["yearEndClosing"] = [exception.Message] });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/year-opening", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        YearOpeningService opening,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(await opening.PreviewAsync(companyId, year, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/year-opening", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        YearOpeningService opening,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(ToMovementResponse(await opening.RegisterAsync(companyId, year, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["yearOpening"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/year-opening/reopen", async (
        Guid userId,
        Guid companyId,
        int year,
        AccountingDbContext context,
        YearOpeningService opening,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(ToMovementResponse(await opening.ReopenAsync(companyId, year, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["yearOpening"] = [exception.Message] });
        }
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

    app.MapGet("/api/v1/development/accounting/taxonomies/{reference}", (string reference) =>
    {
        var normalized = reference.Trim().ToUpperInvariant();
        if (normalized is not ("S" or "M" or "N" or "O"))
        {
            return Results.NotFound();
        }

        var codes = TaxonomyCatalog.For(normalized);
        return Results.Ok(new
        {
            Reference = normalized,
            TaxonomyCatalog.Source,
            TaxonomyCatalog.Disclaimer,
            Codes = codes.Select(item => new
            {
                item.Code,
                item.Description,
                item.SncAccounts
            })
        });
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/journals", async (
        Guid userId,
        Guid companyId,
        CreateJournalRequest request,
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
            var journal = await masterData
                .CreateJournalAsync(companyId, request.Code, request.Name, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(journal);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["journal"] = [exception.Message]
            });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/journals/{code}", async (
        Guid userId,
        Guid companyId,
        string code,
        UpdateJournalRequest request,
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
            var journal = await masterData
                .UpdateJournalAsync(companyId, code, request.Name, request.IsActive, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(journal);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["journal"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/documents", async (
        Guid userId,
        Guid companyId,
        CreateDocumentTypeRequest request,
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
            var documentType = await masterData
                .CreateDocumentTypeAsync(companyId, request.Code, request.Name, request.DefaultJournalCode, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(documentType);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["documentType"] = [exception.Message]
            });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/documents/{code}", async (
        Guid userId,
        Guid companyId,
        string code,
        UpdateDocumentTypeRequest request,
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
            var documentType = await masterData
                .UpdateDocumentTypeAsync(companyId, code, request.Name, request.DefaultJournalCode, request.IsActive, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(documentType);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["documentType"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/saft/inspect", async (
        Guid userId,
        HttpRequest request,
        AccountingDbContext context,
        SaftInspectionService inspection,
        CancellationToken cancellationToken) =>
    {
        var userExists = await context.UserAccounts
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!userExists)
        {
            return Results.NotFound();
        }

        // O leitor consome apenas o elemento Header; ainda assim, um SAF-T real de
        // contabilidade pode ter dezenas de MB, pelo que o limite de corpo é levantado
        // para este pedido de desenvolvimento.
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await inspection
                .InspectAsync(userId, request.Body, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                Header = new
                {
                    result.Header.AuditFileVersion,
                    result.Header.TaxRegistrationNumber,
                    result.Header.TaxAccountingBasis,
                    result.Header.CompanyName,
                    result.Header.BusinessName,
                    result.Header.FiscalYear,
                    result.Header.StartDate,
                    result.Header.EndDate,
                    result.Header.CurrencyCode,
                    result.Header.DateCreated,
                    result.Header.TaxEntity,
                    result.Header.ProductId,
                    result.Header.IncludesAccounting
                },
                Match = new
                {
                    Outcome = result.Match.Outcome.ToString(),
                    result.Match.CompanyId,
                    result.Match.LegalName,
                    result.Match.AccountingStandard,
                    result.Match.AccountingSetupVersion
                }
            });
        }
        catch (SaftFileFormatException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["saft"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/saft/companies", async (
        Guid userId,
        CreateSaftCompanyRequest request,
        AccountingDbContext context,
        SaftCompanyCreationService creation,
        CancellationToken cancellationToken) =>
    {
        var userExists = await context.UserAccounts
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!userExists)
        {
            return Results.NotFound();
        }

        if (!Enum.TryParse<TaxRegion>(request.TaxRegion, true, out var region) || !Enum.IsDefined(region))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TaxRegion)] = ["A região fiscal deve ser Mainland, Azores ou Madeira."]
            });
        }

        if (!Enum.TryParse<AccountingStandard>(request.AccountingStandard, true, out var standard) ||
            !Enum.IsDefined(standard))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.AccountingStandard)] = ["Norma contabilística desconhecida."]
            });
        }

        try
        {
            var created = await creation
                .CreateCompanyAsync(
                    userId,
                    request.TaxRegistrationNumber,
                    request.LegalName,
                    region,
                    standard,
                    string.IsNullOrWhiteSpace(request.BaseCurrency) ? "EUR" : request.BaseCurrency!,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                created.CompanyId,
                created.LegalName,
                created.TaxRegistrationNumber,
                created.AccountingStandard,
                created.AccountingSetupVersion,
                created.AccountCount,
                created.JournalCount,
                created.DocumentTypeCount
            });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["company"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/organization/companies", async (
        CreateOrganizationCompanyRequest request,
        OrganizationAdminService admin,
        CancellationToken cancellationToken) =>
    {
        if (!Enum.TryParse<TaxRegion>(request.TaxRegion, true, out var region) || !Enum.IsDefined(region) ||
            !Enum.TryParse<AccountingStandard>(request.AccountingStandard, true, out var standard) || !Enum.IsDefined(standard))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Região fiscal ou norma contabilística desconhecida."]
            });
        }

        try
        {
            var company = await admin
                .CreateCompanyAsync(
                    request.AccountingFirmId,
                    request.LegalName,
                    request.TaxRegistrationNumber,
                    region,
                    standard,
                    string.IsNullOrWhiteSpace(request.BaseCurrency) ? "EUR" : request.BaseCurrency!,
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(company);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["company"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/organization/companies/{companyId:guid}", async (
        Guid companyId,
        UpdateOrganizationCompanyRequest request,
        OrganizationAdminService admin,
        CancellationToken cancellationToken) =>
    {
        if (!Enum.TryParse<TaxRegion>(request.TaxRegion, true, out var region) || !Enum.IsDefined(region))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["taxRegion"] = ["Região fiscal desconhecida."] });
        }

        DateOnly? postingLockDate = null;
        if (!string.IsNullOrWhiteSpace(request.PostingLockDate))
        {
            if (!DateOnly.TryParseExact(request.PostingLockDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["postingLockDate"] = ["Data de bloqueio inválida. Use o formato AAAA-MM-DD."] });
            }

            postingLockDate = parsed;
        }

        try
        {
            var company = await admin
                .UpdateCompanyAsync(companyId, request.LegalName, region, request.BaseCurrency, request.IsActive, postingLockDate, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(company);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["company"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/organization/users", async (
        CreateOrganizationUserRequest request,
        OrganizationAdminService admin,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var user = await admin.CreateUserAsync(request.DisplayName, request.Email, cancellationToken).ConfigureAwait(false);
            return Results.Ok(user);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/organization/users/{userId:guid}", async (
        Guid userId,
        UpdateOrganizationUserRequest request,
        OrganizationAdminService admin,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var user = await admin
                .UpdateUserAsync(userId, request.DisplayName, request.Email, request.IsActive, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(user);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/organization/companies/{companyId:guid}/members/{userId:guid}", async (
        Guid companyId,
        Guid userId,
        SetMembershipRequest request,
        OrganizationAdminService admin,
        CancellationToken cancellationToken) =>
    {
        if (!Enum.TryParse<CompanyUserRole>(request.Role, true, out var role) || !Enum.IsDefined(role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Função desconhecida."] });
        }

        try
        {
            var membership = await admin
                .SetMembershipAsync(companyId, userId, role, request.IsActive, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(membership);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["membership"] = [exception.Message] });
        }
    });
}

await app.RunAsync().ConfigureAwait(false);

public partial class Program
{
    private static object ToMovementResponse(PostedMovement movement) => new
    {
        Id = movement.Reference,
        Date = movement.EntryDate,
        JournalId = movement.JournalCode,
        movement.JournalName,
        DocumentType = movement.DocumentTypeCode,
        DocumentTypeName = movement.DocumentTypeName,
        Document = movement.DocumentReference,
        movement.Description,
        movement.Notes,
        movement.Status,
        movement.Amount,
        Lines = movement.Lines.Select(line => new
        {
            Account = line.AccountCode,
            line.AccountName,
            line.Description,
            line.Debit,
            line.Credit
        })
    };

    private static bool TryParseReportDateRange(
        string? from,
        string? to,
        out DateOnly? fromDate,
        out DateOnly? toDate,
        out IResult? error)
    {
        fromDate = null;
        toDate = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateOnly.TryParseExact(from.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
            {
                error = Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["from"] = ["Data inicial inválida. Use o formato AAAA-MM-DD."]
                });
                return false;
            }

            fromDate = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateOnly.TryParseExact(to.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
            {
                error = Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["to"] = ["Data final inválida. Use o formato AAAA-MM-DD."]
                });
                return false;
            }

            toDate = parsedTo;
        }

        if (fromDate is not null && toDate is not null && fromDate > toDate)
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["A data final não pode ser anterior à data inicial."]
            });
            return false;
        }

        return true;
    }

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

public sealed record CreateJournalRequest(string Code, string Name);

public sealed record UpdateJournalRequest(string Name, bool IsActive);

public sealed record CreateDocumentTypeRequest(string Code, string Name, string DefaultJournalCode);

public sealed record UpdateDocumentTypeRequest(string Name, string DefaultJournalCode, bool IsActive);

public sealed record UpdateDocumentNumberSeriesRequest(int NextNumber, int? Width = null);

public sealed record CreateSaftCompanyRequest(
    string TaxRegistrationNumber,
    string LegalName,
    string TaxRegion,
    string AccountingStandard,
    string? BaseCurrency);

public sealed record CreateOrganizationCompanyRequest(
    Guid AccountingFirmId,
    string LegalName,
    string TaxRegistrationNumber,
    string TaxRegion,
    string AccountingStandard,
    string? BaseCurrency);

public sealed record UpdateOrganizationCompanyRequest(
    string LegalName,
    string TaxRegion,
    string BaseCurrency,
    bool IsActive,
    string? PostingLockDate = null);

public sealed record CreateOrganizationUserRequest(string DisplayName, string Email);

public sealed record UpdateOrganizationUserRequest(string DisplayName, string Email, bool IsActive);

public sealed record SetMembershipRequest(string Role, bool IsActive);

public sealed record MovementLineRequest(string AccountCode, string Description, decimal Debit, decimal Credit);

public sealed record CreateMovementRequest(
    string JournalCode,
    DateOnly EntryDate,
    string DocumentReference,
    string Description,
    bool Reviewed,
    List<MovementLineRequest> Lines,
    string? DocumentTypeCode = null,
    string? Notes = null);
