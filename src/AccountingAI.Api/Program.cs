using AccountingAI.Api;
using AccountingAI.Application.CrossCheck;
using AccountingAI.Application.Efatura;
using AccountingAI.Application.ManagementReports;
using AccountingAI.Application.Reporting;
using AccountingAI.Application.Saft;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Domain.Saft;
using AccountingAI.Infrastructure.Ai;
using AccountingAI.Infrastructure.At;
using AccountingAI.Infrastructure.Files;
using AccountingAI.Infrastructure.SqlServer;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;

// Ferramenta de linha de comandos: cifra um valor da configuração da AT.
//   dotnet run --project src/AccountingAI.Api -- protect-at-secret "<valor>"
//   (ou passa o valor por stdin). A saída é o valor cifrado a colar em
//   At:Certificate:Password (ou no local do ficheiro).
if (args is ["protect-at-secret", ..])
{
    var plaintext = args.Length >= 2 ? args[1] : (await Console.In.ReadToEndAsync().ConfigureAwait(false)).Trim();
    if (string.IsNullOrEmpty(plaintext))
    {
        await Console.Error
            .WriteLineAsync("Uso: dotnet run --project src/AccountingAI.Api -- protect-at-secret \"<valor>\"")
            .ConfigureAwait(false);
        return;
    }

    Console.WriteLine(AtSecretProtector.Create().Protect(plaintext));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Sem efeito fora do Windows e fora de um serviço (ex.: `dotnet run` local) —
// só muda o comportamento quando o processo é mesmo controlado pelo Service
// Control Manager (F2-11B: publicar + `sc.exe create`, ver scripts/publish-service.ps1).
builder.Host.UseWindowsService(options => options.ServiceName = "AccountingAI");

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddProblemDetails();
builder.Services.AddAccountingSqlServer(builder.Configuration);
builder.Services.AddSaftFileReading();
builder.Services.AddAccountingAi(builder.Configuration);
builder.Services.AddAccountingAt(builder.Configuration);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "AccountingAI.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // API, não uma app com páginas: em vez de redirecionar para um ecrã de
        // login, devolve o estado HTTP — a PWA decide o que mostrar.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    // Todo o endpoint exige sessão autenticada, salvo `.AllowAnonymous()`
    // explícito (login, health checks, fundação) — ver ADR-0011.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

app.UseExceptionHandler();
// Ficheiros estáticos da PWA (F2-11B) — servidos do `wwwroot` do próprio
// processo, para o serviço do Windows não depender de um segundo servidor
// (o Vite é só de desenvolvimento). Middleware, não endpoint: não passa pela
// autorização, tal como o `index.html` tem de ser servível sem sessão para
// mostrar o ecrã de login. Vazio em desenvolvimento até se publicar.
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<CurrentUserMiddleware>();

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

// Ferramenta de linha de comandos: define/substitui a senha de um utilizador.
//   dotnet run --project src/AccountingAI.Api -- set-user-password "<email>" "<senha>"
//   (ou a senha por stdin). Nunca há um endpoint público de registo — só esta
//   ferramenta, correndo localmente, define uma senha.
if (args is ["set-user-password", ..])
{
    if (args.Length < 2)
    {
        await Console.Error
            .WriteLineAsync("Uso: dotnet run --project src/AccountingAI.Api -- set-user-password \"<email>\" \"<senha>\"")
            .ConfigureAwait(false);
        return;
    }

    var email = args[1];
    var password = args.Length >= 3 ? args[2] : (await Console.In.ReadToEndAsync().ConfigureAwait(false)).Trim();
    if (string.IsNullOrEmpty(password))
    {
        await Console.Error.WriteLineAsync("A senha não pode estar vazia.").ConfigureAwait(false);
        return;
    }

    await using var passwordScope = app.Services.CreateAsyncScope();
    var authService = passwordScope.ServiceProvider.GetRequiredService<AuthService>();
    try
    {
        await authService.SetPasswordAsync(email, password).ConfigureAwait(false);
        Console.WriteLine($"Senha atualizada para {email}.");
    }
    catch (InvalidOperationException exception)
    {
        await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    }

    return;
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

app.MapGet("/api/v1/foundation", () => Results.Ok(new
{
    service = "AccountingAI.Api",
    phase = "F2-01",
    status = "ready"
})).AllowAnonymous();

app.MapPost("/api/v1/auth/login", async (
    LoginRequest request,
    AuthService authService,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["credentials"] = ["Indique o email e a senha."],
        });
    }

    var user = await authService.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken)
        .ConfigureAwait(false);
    if (user is null)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["credentials"] = ["Email ou senha incorretos."] },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.DisplayName),
        new Claim(ClaimTypes.Email, user.Email),
    };
    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await context
        .SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true })
        .ConfigureAwait(false);

    return Results.Ok(new { id = user.Id, displayName = user.DisplayName, email = user.Email });
}).AllowAnonymous();

app.MapPost("/api/v1/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
    return Results.Ok();
}).AllowAnonymous();

app.MapGet("/api/v1/auth/me", (HttpContext context) => Results.Ok(new
{
    id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
    displayName = context.User.FindFirstValue(ClaimTypes.Name),
    email = context.User.FindFirstValue(ClaimTypes.Email),
}));

// Até à F2-11A este bloco só existia em Development — a única barreira de
// toda a API, sem autenticação nenhuma (ver ADR-0011). A sessão de F2-11A
// tornou essa barreira desnecessária: os endpoints ficam sempre disponíveis,
// agora protegidos por login (FallbackPolicy) em vez de por ambiente — o que
// deixa também de impedir correr fora de Development (ex.: como serviço do
// Windows, F2-11B).
var applicationEndpointsAvailable = true;
if (applicationEndpointsAvailable)
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
                        company.AddressDetail,
                        company.City,
                        company.PostalCode,
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
                                        membership.Settings.PageSize,
                                        TextSize = membership.Settings.TextSize.ToString()
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
                    Settings = ToSettingsResponse(item.Settings) ?? DefaultSettingsResponse()
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

        if (!Enum.TryParse<InterfaceTextSize>(request.TextSize, true, out var textSize))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TextSize)] = ["O tamanho do texto deve ser Normal, Large ou Larger."]
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
                request.PageSize,
                textSize);

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
        string? documentType,
        string? source,
        string? journal,
        string? search,
        string? status,
        string? from,
        string? to,
        int? page,
        int? pageSize) =>
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

        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var query = context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Include(item => item.Journal)
            .Include(item => item.DocumentType)
            .Include(item => item.Lines)
                .ThenInclude(item => item.Account)
            .Include(item => item.Lines)
                .ThenInclude(item => item.Party)
            .Include(item => item.Lines)
                .ThenInclude(item => item.VatCode)
            .Include(item => item.Lines)
                .ThenInclude(item => item.CostCenter)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            var normalizedDocumentType = documentType.Trim().ToUpperInvariant();
            query = query.Where(item => item.DocumentType != null && item.DocumentType.Code == normalizedDocumentType);
        }

        if (!string.IsNullOrWhiteSpace(source) && Enum.TryParse<EntrySource>(source.Trim(), ignoreCase: true, out var entrySource))
        {
            query = query.Where(item => item.Source == entrySource);
        }

        if (!string.IsNullOrWhiteSpace(journal))
        {
            var normalizedJournal = journal.Trim().ToUpperInvariant();
            query = query.Where(item => item.Journal.Code == normalizedJournal);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim() switch
            {
                "Conferido" => (JournalEntryStatus?)JournalEntryStatus.Reviewed,
                "Anulado" => JournalEntryStatus.Voided,
                "Por conferir" => JournalEntryStatus.PendingReview,
                _ => null
            };
            if (wanted is not null)
            {
                query = query.Where(item => item.Status == wanted);
            }
        }

        if (fromDate is { } lowerBound)
        {
            query = query.Where(item => item.EntryDate >= lowerBound);
        }

        if (toDate is { } upperBound)
        {
            query = query.Where(item => item.EntryDate <= upperBound);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item =>
                item.Reference.Contains(term) ||
                item.DocumentReference.Contains(term) ||
                item.Description.Contains(term) ||
                (item.Notes != null && item.Notes.Contains(term)));
        }

        var ordered = query
            .OrderByDescending(item => item.EntryDate)
            .ThenByDescending(item => item.Reference);

        // Sem `page` a resposta continua a ser o array completo, para o contexto
        // de trabalho e os relatórios que ainda leem tudo. Com `page`, o SQL
        // Server pagina — nas empresas grandes (SAF-T) não se transferem 20 MB.
        if (page is null)
        {
            var allEntities = await ordered
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(allEntities.Select(ToMovementPayload));
        }

        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var totalItems = await ordered.CountAsync(cancellationToken).ConfigureAwait(false);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)size));
        var currentPage = Math.Clamp(page.Value, 1, totalPages);

        var pageEntities = await ordered
            .Skip((currentPage - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            Items = pageEntities.Select(ToMovementPayload),
            Page = currentPage,
            PageSize = size,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    });

    static object ToMovementPayload(JournalEntry item)
    {
        return new
        {
            Id = item.Reference,
            Date = item.EntryDate,
            JournalId = item.Journal.Code,
            JournalName = item.Journal.Name,
            DocumentType = item.DocumentType == null ? null : item.DocumentType.Code,
            DocumentTypeName = item.DocumentType == null ? null : item.DocumentType.Name,
            Document = item.DocumentReference,
            item.DocumentDate,
            item.Description,
            item.Notes,
            Status = item.Status switch
            {
                JournalEntryStatus.Reviewed => "Conferido",
                JournalEntryStatus.Voided => "Anulado",
                _ => "Por conferir"
            },
            Source = item.Source.ToString(),
            Amount = item.TotalAmount,
            Lines = item.Lines
                .OrderBy(line => line.Sequence)
                .Select(line => new
                {
                    Account = line.Account.Code,
                    AccountName = line.Account.Name,
                    line.Description,
                    line.Debit,
                    line.Credit,
                    PartyCountryCode = line.Party?.CountryCode,
                    PartyTaxNumber = line.Party?.TaxRegistrationNumber,
                    PartyName = line.Party?.Name,
                    VatCode = line.VatCode?.Code,
                    CostCenter = line.CostCenter?.Code
                })
        };
    }

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements/summary", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        // Os números do estado da fila de trabalho (barra de topo, Tarefas) sem
        // o array inteiro de movimentos: contagens e somas agregadas no SQL.
        var entries = context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId);

        var total = await entries.CountAsync(cancellationToken).ConfigureAwait(false);
        var pendingReview = await entries
            .CountAsync(item => item.Status == JournalEntryStatus.PendingReview, cancellationToken)
            .ConfigureAwait(false);
        var voided = await entries
            .CountAsync(item => item.Status == JournalEntryStatus.Voided, cancellationToken)
            .ConfigureAwait(false);

        var vatLines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.JournalEntry.Status != JournalEntryStatus.Voided);

        var vatInput = await vatLines
            .Where(line => line.Account.Code.StartsWith("2432"))
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;
        var vatOutput = await vatLines
            .Where(line => line.Account.Code.StartsWith("2433"))
            .SumAsync(line => (decimal?)(line.Credit - line.Debit), cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var pendingPreview = await entries
            .Where(item => item.Status == JournalEntryStatus.PendingReview)
            .OrderBy(item => item.EntryDate)
            .ThenBy(item => item.Reference)
            .Take(7)
            .Select(item => new
            {
                Id = item.Reference,
                Date = item.EntryDate,
                item.Description,
                Amount = item.TotalAmount
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            Total = total,
            PendingReview = pendingReview,
            Voided = voided,
            VatInput = vatInput,
            VatOutput = vatOutput,
            VatPosition = vatOutput - vatInput,
            PendingPreview = pendingPreview
        });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/journal-movement-counts", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        // Contagem de lançamentos por diário, para a coluna «Movimentos» do
        // ecrã de diários não precisar do array inteiro.
        var counts = await context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .GroupBy(item => item.Journal.Code)
            .Select(group => new { Code = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(counts.ToDictionary(item => item.Code, item => item.Count));
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements/{reference}", async (
        Guid userId,
        Guid companyId,
        string reference,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        // Um lançamento pela referência, para o ecrã de detalhe não depender do
        // array completo (`/movements/summary` tem precedência sobre este).
        var entity = await context.JournalEntries
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Reference == reference)
            .Include(item => item.Journal)
            .Include(item => item.DocumentType)
            .Include(item => item.Lines)
                .ThenInclude(item => item.Account)
            .Include(item => item.Lines)
                .ThenInclude(item => item.Party)
            .Include(item => item.Lines)
                .ThenInclude(item => item.VatCode)
            .Include(item => item.Lines)
                .ThenInclude(item => item.CostCenter)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? Results.NotFound() : Results.Ok(ToMovementPayload(entity));
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
                    line.Credit,
                    line.PartyTaxNumber,
                    line.PartyCountryCode,
                    line.VatCode,
                    line.CostCenter))
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
                    request.Notes,
                    documentDate: request.DocumentDate)
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
                .Select(line => new JournalEntryLineInput(
                    line.AccountCode, line.Description, line.Debit, line.Credit, line.PartyTaxNumber, line.PartyCountryCode, line.VatCode, line.CostCenter))
                .ToList();
            var movement = await posting
                .ReviseAsync(companyId, reference, request.JournalCode, request.EntryDate,
                    request.DocumentReference, request.Description, request.Reviewed, lines, cancellationToken,
                    request.DocumentTypeCode, request.Notes, request.DocumentDate)
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

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/accounting/movements/{reference}/reopen", async (
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
            var movement = await posting.ReopenAsync(companyId, reference, cancellationToken).ConfigureAwait(false);
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
        string? to,
        string? account,
        string? source) =>
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
            .BuildAsync(companyId, journal, documentType, year, fromDate, toDate, cancellationToken, account, source)
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
                Source = entry.Source.ToString(),
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
        string? to,
        string? source) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var result = await ledger.AccountLedgerAsync(companyId, code, fromDate, toDate, cancellationToken, source).ConfigureAwait(false);
        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            result.AccountCode,
            result.AccountName,
            result.IsAggregate,
            result.OpeningBalance,
            result.Balance,
            Entries = result.Entries.Select(entry => new
            {
                entry.Reference,
                entry.EntryDate,
                entry.JournalCode,
                entry.AccountCode,
                entry.Description,
                entry.Notes,
                entry.Debit,
                entry.Credit,
                entry.RunningBalance,
                Source = entry.Source.ToString()
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

    app.MapGet("/api/v1/development/data-dictionary", (DataDictionaryService dictionary) =>
        Results.Ok(new { Tables = dictionary.ListTables() }));

    app.MapGet("/api/v1/development/data-dictionary/{schema}/{table}", (
        string schema, string table, DataDictionaryService dictionary) =>
    {
        var detail = dictionary.GetTable(schema, table);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    });

    app.MapGet("/api/v1/development/analyses/catalog", () =>
        Results.Json(new
        {
            Sources = AnalysisFieldCatalog.Sources.Select(source => new
            {
                Key = source.ToString(),
                Fields = AnalysisFieldCatalog.Fields(source).Select(field => new
                {
                    field.Key,
                    field.Label,
                    Type = field.Type.ToString(),
                    field.Groupable,
                    field.Aggregatable,
                }),
            }),
            OperatorsByType = AnalysisFieldCatalog.OperatorsByType,
        }, AnalysisJson.Options));

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/analyses", async (
        Guid userId, Guid companyId, SavedAnalysisService analyses, CancellationToken cancellationToken) =>
    {
        var list = await analyses.ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Json(list, AnalysisJson.Options);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/analyses", async (
        Guid userId, Guid companyId, HttpRequest request, SavedAnalysisService analyses, CancellationToken cancellationToken) =>
    {
        var body = await ReadAnalysisRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (body?.Definition is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["analise"] = ["Indique o nome e a definição da análise."]
            });
        }

        var result = await analyses
            .CreateAsync(userId, companyId, body.Name, body.Description, body.Definition, cancellationToken)
            .ConfigureAwait(false);
        return AnalysisWriteResult(result);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/analyses/{id:guid}", async (
        Guid userId, Guid companyId, Guid id, HttpRequest request, SavedAnalysisService analyses, CancellationToken cancellationToken) =>
    {
        var body = await ReadAnalysisRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (body?.Definition is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["analise"] = ["Indique o nome e a definição da análise."]
            });
        }

        var result = await analyses
            .UpdateAsync(userId, companyId, id, body.Name, body.Description, body.Definition, cancellationToken)
            .ConfigureAwait(false);
        return AnalysisWriteResult(result);
    });

    app.MapDelete("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/analyses/{id:guid}", async (
        Guid userId, Guid companyId, Guid id, SavedAnalysisService analyses, CancellationToken cancellationToken) =>
    {
        var deleted = await analyses.DeleteAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false);
        return deleted ? Results.NoContent() : Results.NotFound();
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/analyses/run", async (
        Guid userId, Guid companyId, HttpRequest request, IAnalysisRunner runner,
        CancellationToken cancellationToken, string? from, string? to) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var body = await ReadAnalysisRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (body?.Definition is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["analise"] = ["Indique a definição da análise a correr."]
            });
        }

        return await RunAnalysisAsync(runner, userId, companyId, body.Definition, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/report-templates/{reportKey}", async (
        Guid userId, Guid companyId, string reportKey, ReportTemplateService templates, CancellationToken cancellationToken) =>
    {
        var view = await templates.GetAsync(userId, companyId, reportKey, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/report-templates/{reportKey}", async (
        Guid userId, Guid companyId, string reportKey, PutReportTemplateRequest request,
        ReportTemplateService templates, CancellationToken cancellationToken) =>
    {
        var view = await templates
            .SaveAsync(
                userId, companyId, reportKey,
                request.HeaderLines ?? [],
                request.ShowFirmName,
                request.ShowCompanyIdentity,
                request.Orientation,
                cancellationToken)
            .ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/analyses/{id:guid}/run", async (
        Guid userId, Guid companyId, Guid id, SavedAnalysisService analyses, IAnalysisRunner runner,
        CancellationToken cancellationToken, string? from, string? to) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var saved = await analyses.GetAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false);
        if (saved is null)
        {
            return Results.NotFound();
        }

        return await RunAnalysisAsync(runner, userId, companyId, saved.Definition, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);
    });

    // Tabelas fiscais por ano: valores propostos, com a fonte de cada grupo.
    app.MapGet("/api/v1/development/fiscal-tables/{year:int}", (int year) =>
        AccountingAI.Application.Fiscal.FiscalTableCatalog.For(year) is { } table ? Results.Ok(table) : Results.NotFound());

    app.MapGet("/api/v1/development/at/certificate", (AccountingAI.Application.At.IAtCertificateProvider certificates) =>
        Results.Ok(certificates.Describe()));

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/at-credentials", async (
        Guid userId, Guid companyId, CompanyAtCredentialsService credentials, CancellationToken cancellationToken) =>
    {
        var view = await credentials.GetAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/at-credentials", async (
        Guid userId, Guid companyId, PutAtCredentialsRequest request,
        CompanyAtCredentialsService credentials, CancellationToken cancellationToken) =>
    {
        var subUser = request?.SubUser?.Trim();
        var password = request?.Password;
        if (string.IsNullOrWhiteSpace(subUser) || string.IsNullOrWhiteSpace(password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credenciais"] = ["Indique o subutilizador e a senha."]
            });
        }

        if (!TryParseAtSettings(request?.Periodicity, request?.Environment, out var periodicity, out var environment, out var error))
        {
            return error!;
        }

        var view = await credentials
            .SaveAsync(userId, companyId, subUser, password, periodicity, environment, cancellationToken)
            .ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapDelete("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/at-credentials", async (
        Guid userId, Guid companyId, CompanyAtCredentialsService credentials, CancellationToken cancellationToken) =>
    {
        var removed = await credentials.DeleteAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return removed ? Results.NoContent() : Results.NotFound();
    });

    app.MapGet("/api/v1/development/management-reports/catalog", () =>
        Results.Ok(new
        {
            Kinds = ManagementReportCatalog.Entries.Select(entry => new
            {
                entry.Key,
                entry.Label,
                entry.Description,
                entry.Sections,
            }),
        }));

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management-reports/dossier", async (
        Guid userId, Guid companyId, string? kind, string? from, string? to,
        IManagementReportDataService data, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var request = new ManagementReportRequest(
            ManagementReportCatalog.Parse(kind), null, periodFrom, periodTo);
        var dossier = await data
            .BuildDossierAsync(userId, companyId, request, cancellationToken)
            .ConfigureAwait(false);
        return dossier is null ? Results.NotFound() : Results.Ok(dossier);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/ai-settings", async (
        Guid userId, Guid companyId, CompanyAiSettingsService settings, CancellationToken cancellationToken) =>
    {
        var view = await settings.GetAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/ai-settings", async (
        Guid userId, Guid companyId, PutAiSettingsRequest request,
        CompanyAiSettingsService settings, CancellationToken cancellationToken) =>
    {
        var view = await settings
            .SaveAsync(userId, companyId, request.Enabled, request.AutoMonthly, cancellationToken)
            .ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management-reports/generate", async (
        Guid userId, Guid companyId, string? from, string? to, GenerateManagementReportRequest? request,
        ManagementReportService reports, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var kind = ManagementReportCatalog.Parse(request?.Kind);
        var question = request?.Question?.Trim();
        if (kind is null && string.IsNullOrWhiteSpace(question))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pedido"] = ["Indique um tipo de relatório pré-definido ou uma pergunta."]
            });
        }

        var outcome = await reports
            .GenerateAsync(
                userId, companyId,
                new ManagementReportRequest(kind, question, periodFrom, periodTo),
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.NotFound)
        {
            return Results.NotFound();
        }

        return outcome.Disabled
            ? Results.Problem(outcome.DisabledReason, statusCode: StatusCodes.Status409Conflict)
            : Results.Ok(outcome.View);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management-reports", async (
        Guid userId, Guid companyId, ManagementReportService reports, CancellationToken cancellationToken) =>
    {
        var list = await reports.ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management-reports/{id:guid}", async (
        Guid userId, Guid companyId, Guid id, ManagementReportService reports, CancellationToken cancellationToken) =>
    {
        var view = await reports.GetAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    // Os quadros 04 e 05 da declaração recapitulativa de IVA.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/recapitulative-statement", async (
        Guid userId, Guid companyId, string? from, string? to,
        RecapitulativeStatementService statements, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) ||
            end < start)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["periodo"] = ["Indique um período válido (aaaa-mm-dd)."] });
        }

        var statement = await statements.BuildAsync(userId, companyId, start, end, cancellationToken).ConfigureAwait(false);
        return statement is null ? Results.NotFound() : Results.Ok(statement);
    });

    // O quadro 06 da declaração periódica de IVA, preenchido a partir da contabilidade.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-declaration", async (
        Guid userId, Guid companyId, string? from, string? to, decimal? priorCredit,
        VatDeclarationService declarations, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) ||
            end < start)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["periodo"] = ["Indique um período válido (aaaa-mm-dd)."] });
        }

        if (priorCredit is < 0m)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["priorCredit"] = ["O excesso a reportar não pode ser negativo."] });
        }

        var declaration = await declarations.BuildAsync(userId, companyId, start, end, priorCredit, cancellationToken).ConfigureAwait(false);
        return declaration is null ? Results.NotFound() : Results.Ok(declaration);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-return", async (
        Guid userId, Guid companyId, string? from, string? to,
        AccountingAI.Application.Vat.IVatReturnService vat, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var result = await vat
            .BuildAsync(userId, companyId, periodFrom, periodTo, null, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-return/saved", async (
        Guid userId, Guid companyId, string? from, string? to,
        VatReturnFilingService filings, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var view = await filings.GetAsync(userId, companyId, periodFrom, periodTo, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-return/saved", async (
        Guid userId, Guid companyId, string? from, string? to, SaveVatReturnRequest request,
        VatReturnFilingService filings, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var view = await filings
            .SaveAsync(userId, companyId, periodFrom, periodTo, request?.PriorPeriodCredit ?? 0m, cancellationToken)
            .ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-return/history", async (
        Guid userId, Guid companyId, VatReturnFilingService filings, CancellationToken cancellationToken) =>
    {
        var list = await filings.ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-classification", async (
        Guid userId, Guid companyId, AccountVatClassificationService classifications, CancellationToken cancellationToken) =>
    {
        var list = await classifications.ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-classification/{accountCode}", async (
        Guid userId, Guid companyId, string accountCode, PutVatClassificationRequest request,
        AccountVatClassificationService classifications, CancellationToken cancellationToken) =>
    {
        if (!TryParseVatClassification(request?.Rate, request?.Nature, request?.Region, out var rate, out var nature, out var region, out var error))
        {
            return error!;
        }

        var ok = await classifications
            .SetAsync(userId, companyId, accountCode, rate, nature, region, cancellationToken)
            .ConfigureAwait(false);
        return ok ? Results.NoContent() : Results.NotFound();
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-classification/bulk", async (
        Guid userId, Guid companyId, BulkVatClassificationRequest request,
        AccountVatClassificationService classifications, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request?.Prefix))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["prefixo"] = ["Indique um prefixo de conta."] });
        }

        if (!TryParseVatClassification(request.Rate, request.Nature, request.Region, out var rate, out var nature, out var region, out var error))
        {
            return error!;
        }

        var count = await classifications
            .BulkSetAsync(userId, companyId, request.Prefix, rate, nature, region, cancellationToken)
            .ConfigureAwait(false);
        return count < 0 ? Results.NotFound() : Results.Ok(new { updated = count });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/parties", async (
        Guid userId, Guid companyId, string? search, string? role, bool? includeInactive, int? page, int? pageSize, string? accountCode,
        PartyService parties, CancellationToken cancellationToken) =>
    {
        var roleFilter = PartyRoleFilter.Todos;
        if (!string.IsNullOrWhiteSpace(role) &&
            !(Enum.TryParse(role.Trim(), ignoreCase: true, out roleFilter) && Enum.IsDefined(roleFilter)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["funcao"] = ["Função inválida."] });
        }

        var result = await parties
            .ListAsync(userId, companyId, search, roleFilter, includeInactive ?? false, page ?? 1, pageSize ?? 50, cancellationToken, accountCode)
            .ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/parties/balances", async (
        Guid userId, Guid companyId, string? to, string? search, int? page, int? pageSize,
        PartyLedgerService ledger, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(null, to, out _, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var result = await ledger
            .BalancesAsync(userId, companyId, toDate, search, page ?? 1, pageSize ?? 50, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/parties/{partyId:guid}/statement", async (
        Guid userId, Guid companyId, Guid partyId, string? from, string? to, int? page, int? pageSize,
        PartyLedgerService ledger, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var result = await ledger
            .StatementAsync(userId, companyId, partyId, fromDate, toDate, page ?? 1, pageSize ?? 50, cancellationToken)
            .ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/parties", async (
        Guid userId, Guid companyId, CreatePartyRequest request, PartyService parties, CancellationToken cancellationToken) =>
    {
        try
        {
            var created = await parties
                .CreateAsync(
                    userId, companyId, request.CountryCode, request.TaxRegistrationNumber ?? string.Empty,
                    new PartyInput(
                        request.Name ?? string.Empty, request.IsCustomer, request.IsSupplier,
                        request.CustomerAccountCode, request.SupplierAccountCode,
                        AddressDetail: request.AddressDetail, City: request.City, PostalCode: request.PostalCode),
                    cancellationToken)
                .ConfigureAwait(false);
            return created is null ? Results.NotFound() : Results.Ok(created);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["terceiro"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/parties/{partyId:guid}", async (
        Guid userId, Guid companyId, Guid partyId, UpdatePartyRequest request, PartyService parties, CancellationToken cancellationToken) =>
    {
        try
        {
            var updated = await parties
                .UpdateAsync(
                    userId, companyId, partyId,
                    new PartyInput(
                        request.Name ?? string.Empty, request.IsCustomer, request.IsSupplier,
                        request.CustomerAccountCode, request.SupplierAccountCode, request.IsActive,
                        request.AddressDetail, request.City, request.PostalCode),
                    cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["terceiro"] = [exception.Message] });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-codes", async (
        Guid userId, Guid companyId, bool? includeInactive, VatCodeService vatCodes, CancellationToken cancellationToken) =>
    {
        var list = await vatCodes.ListAsync(userId, companyId, includeInactive ?? false, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-codes/defaults", async (
        Guid userId, Guid companyId, VatCodeService vatCodes, CancellationToken cancellationToken) =>
    {
        var created = await vatCodes.EnsureDefaultsAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return created < 0 ? Results.NotFound() : Results.Ok(new { created });
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-codes", async (
        Guid userId, Guid companyId, VatCodeRequest request, VatCodeService vatCodes, CancellationToken cancellationToken) =>
    {
        if (!TryParseVatClassification(request?.Rate, request?.Nature, request?.Region, out var rate, out var nature, out var region, out var error))
        {
            return error!;
        }

        try
        {
            var created = await vatCodes
                .CreateAsync(
                    userId, companyId, request!.Code ?? string.Empty,
                    new VatCodeInput(request.Name ?? string.Empty, rate, request.Percentage, nature, region),
                    cancellationToken)
                .ConfigureAwait(false);
            return created is null ? Results.NotFound() : Results.Ok(created);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["codigoIva"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-codes/{code}", async (
        Guid userId, Guid companyId, string code, VatCodeRequest request, VatCodeService vatCodes, CancellationToken cancellationToken) =>
    {
        if (!TryParseVatClassification(request?.Rate, request?.Nature, request?.Region, out var rate, out var nature, out var region, out var error))
        {
            return error!;
        }

        try
        {
            var updated = await vatCodes
                .UpdateAsync(
                    userId, companyId, code,
                    new VatCodeInput(request!.Name ?? string.Empty, rate, request.Percentage, nature, region, request.IsActive),
                    cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["codigoIva"] = [exception.Message] });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/cost-centers", async (
        Guid userId, Guid companyId, bool? includeInactive, CostCenterService costCenters, CancellationToken cancellationToken) =>
    {
        var list = await costCenters.ListAsync(userId, companyId, includeInactive ?? false, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/cost-centers/report", async (
        Guid userId, Guid companyId, string? from, string? to, CostCenterService costCenters, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var report = await costCenters.ReportAsync(userId, companyId, fromDate, toDate, cancellationToken).ConfigureAwait(false);
        return report is null ? Results.NotFound() : Results.Ok(report);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/cost-centers", async (
        Guid userId, Guid companyId, CostCenterRequest request, CostCenterService costCenters, CancellationToken cancellationToken) =>
    {
        try
        {
            var created = await costCenters
                .CreateAsync(userId, companyId, request?.Code ?? string.Empty, request?.Name ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return created is null ? Results.NotFound() : Results.Ok(created);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["centroCusto"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/cost-centers/{code}", async (
        Guid userId, Guid companyId, string code, CostCenterRequest request, CostCenterService costCenters, CancellationToken cancellationToken) =>
    {
        try
        {
            var updated = await costCenters
                .UpdateAsync(userId, companyId, code, request?.Name ?? string.Empty, request?.IsActive ?? true, cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["centroCusto"] = [exception.Message] });
        }
    });

    // Ativos fixos: cadastro e mapa de depreciações de um exercício.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/fixed-assets", async (
        Guid userId, Guid companyId, bool? includeInactive, FixedAssetService assets, CancellationToken cancellationToken) =>
    {
        var list = await assets.ListAsync(userId, companyId, includeInactive ?? false, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/fixed-assets/schedule", async (
        Guid userId, Guid companyId, int year, FixedAssetService assets, CancellationToken cancellationToken) =>
    {
        if (year is < 2000 or > 9999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Indique um exercício válido."] });
        }

        var schedule = await assets.ScheduleAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        return schedule is null ? Results.NotFound() : Results.Ok(schedule);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/fixed-assets/schedule/post", async (
        Guid userId, Guid companyId, int year, string? journal, bool? allowExisting, FixedAssetService assets, CancellationToken cancellationToken) =>
    {
        if (year is < 2000 or > 9999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Indique um exercício válido."] });
        }

        try
        {
            var posted = await assets
                .PostDepreciationAsync(userId, companyId, year, journal ?? string.Empty, allowExisting ?? false, cancellationToken)
                .ConfigureAwait(false);
            return posted is null ? Results.NotFound() : Results.Ok(ToMovementResponse(posted));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["depreciacoes"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/fixed-assets", async (
        Guid userId, Guid companyId, FixedAssetInput request, FixedAssetService assets, CancellationToken cancellationToken) =>
    {
        try
        {
            var created = await assets.CreateAsync(userId, companyId, request, cancellationToken).ConfigureAwait(false);
            return created is null ? Results.NotFound() : Results.Ok(created);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ativo"] = [exception.Message] });
        }
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/fixed-assets/{code}", async (
        Guid userId, Guid companyId, string code, FixedAssetInput request, FixedAssetService assets, CancellationToken cancellationToken) =>
    {
        try
        {
            var updated = await assets.UpdateAsync(userId, companyId, code, request, cancellationToken).ConfigureAwait(false);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ativo"] = [exception.Message] });
        }
    });

    // Demonstrações financeiras nos modelos do SNC, montadas pelas taxonomias das contas.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/statements/balance-sheet", async (
        Guid userId, Guid companyId, string? date, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var at))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Indique a data do balanço (aaaa-mm-dd)."] });
        }

        var statement = await statements.BalanceSheetAsync(userId, companyId, at, cancellationToken).ConfigureAwait(false);
        return statement is null ? Results.NotFound() : Results.Ok(statement);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/statements/income-statement", async (
        Guid userId, Guid companyId, string? from, string? to, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) ||
            end < start)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Indique um período válido (aaaa-mm-dd)."] });
        }

        var statement = await statements.IncomeStatementAsync(userId, companyId, start, end, cancellationToken).ConfigureAwait(false);
        return statement is null ? Results.NotFound() : Results.Ok(statement);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/statements/cash-flows", async (
        Guid userId, Guid companyId, string? from, string? to, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) ||
            end < start)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Indique um período válido (aaaa-mm-dd)."] });
        }

        var statement = await statements.CashFlowsAsync(userId, companyId, start, end, cancellationToken).ConfigureAwait(false);
        return statement is null ? Results.NotFound() : Results.Ok(statement);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/statements/equity-changes", async (
        Guid userId, Guid companyId, string? from, string? to, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) ||
            end < start ||
            start.Year != end.Year)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Indique um período válido dentro do mesmo exercício (aaaa-mm-dd)."] });
        }

        var statement = await statements.EquityChangesAsync(userId, companyId, start, end, cancellationToken).ConfigureAwait(false);
        return statement is null ? Results.NotFound() : Results.Ok(statement);
    });

    // Evolução mensal de um exercício, para os gráficos de gestão.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management/monthly-evolution", async (
        Guid userId, Guid companyId, int year, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (year is < 2000 or > 9999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Indique um exercício válido."] });
        }

        var evolution = await statements.MonthlyEvolutionAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        return evolution is null ? Results.NotFound() : Results.Ok(evolution);
    });

    // Mapa de exploração mensal e rácios, sobre as rubricas das demonstrações financeiras.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management/operating-map", async (
        Guid userId, Guid companyId, int year, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (year is < 2000 or > 9999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Indique um exercício válido."] });
        }

        var map = await statements.OperatingMapAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        return map is null ? Results.NotFound() : Results.Ok(map);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/management/ratios", async (
        Guid userId, Guid companyId, string? from, string? to, FinancialStatementsService statements, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) ||
            end < start ||
            start.Year != end.Year)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Indique um período válido dentro do mesmo exercício (aaaa-mm-dd)."] });
        }

        var ratios = await statements.RatiosAsync(userId, companyId, start, end, cancellationToken).ConfigureAwait(false);
        return ratios is null ? Results.NotFound() : Results.Ok(ratios);
    });

    // Exportação do SAF-T (PT) de contabilidade de um exercício: primeiro a análise, depois o ficheiro.
    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/export-report", async (
        Guid userId, Guid companyId, int year, SaftExportService export, CancellationToken cancellationToken) =>
    {
        if (year is < 2000 or > 9999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Indique um exercício válido."] });
        }

        var report = await export.ReportAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        return report is null ? Results.NotFound() : Results.Ok(report);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/export", async (
        Guid userId, Guid companyId, int year, SaftExportService export, CancellationToken cancellationToken) =>
    {
        if (year is < 2000 or > 9999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Indique um exercício válido."] });
        }

        // O ficheiro é montado em memória para só ser servido se não houver problemas bloqueantes.
        var buffer = new MemoryStream();
        var report = await export.ExportAsync(userId, companyId, year, buffer, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Results.NotFound();
        }

        if (!report.CanExport)
        {
            return Results.Json(report, statusCode: StatusCodes.Status409Conflict);
        }

        buffer.Position = 0;
        return Results.File(buffer, "application/xml", $"SAFT-PT-contabilidade-{year}.xml");
    });

    // Ligar os terceiros de um SAF-T aos lançamentos já importados: «preview» só analisa.
    foreach (var (route, apply) in new[] { ("parties-link-preview", false), ("parties-link", true) })
    {
        app.MapPost($"/api/v1/development/users/{{userId:guid}}/companies/{{companyId:guid}}/saft/{route}", async (
            Guid userId, Guid companyId, HttpRequest request, SaftEntriesImportService import, CancellationToken cancellationToken) =>
        {
            var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxBodySize is { IsReadOnly: false })
            {
                maxBodySize.MaxRequestBodySize = null;
            }

            try
            {
                var result = await import
                    .LinkPartiesAsync(userId, companyId, request.Body, apply, cancellationToken)
                    .ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (SaftFileFormatException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["saft"] = [exception.Message] });
            }
        });
    }

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/entry-templates", async (
        Guid userId, Guid companyId, bool? includeInactive, EntryTemplateService templates, CancellationToken cancellationToken) =>
    {
        var list = await templates.ListAsync(userId, companyId, includeInactive ?? false, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/entry-templates/{code}", async (
        Guid userId, Guid companyId, string code, EntryTemplateRequest request, EntryTemplateService templates, CancellationToken cancellationToken) =>
    {
        var lines = new List<EntryTemplateLineInput>();
        foreach (var line in request?.Lines ?? [])
        {
            if (!Enum.TryParse<AccountingAI.Domain.Accounting.EntryTemplateLineKind>(line.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) ||
                !Enum.TryParse<AccountingAI.Domain.Accounting.EntryTemplateSide>(line.Side, ignoreCase: true, out var side) || !Enum.IsDefined(side))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["lancamentoTipo"] = ["Tipo ou lado de linha inválido."] });
            }

            lines.Add(new EntryTemplateLineInput(kind, side, line.AccountCode ?? string.Empty, line.VatCode, line.Description));
        }

        try
        {
            var saved = await templates
                .SaveAsync(
                    userId, companyId, code,
                    new EntryTemplateInput(
                        request?.Name ?? string.Empty, request?.JournalCode ?? string.Empty, request?.DocumentTypeCode,
                        request?.Description, request?.IsActive ?? true, lines),
                    cancellationToken)
                .ConfigureAwait(false);
            return saved is null ? Results.NotFound() : Results.Ok(saved);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["lancamentoTipo"] = [exception.Message] });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/entry-templates/{code}/apply", async (
        Guid userId, Guid companyId, string code, ApplyEntryTemplateRequest request, EntryTemplateService templates, CancellationToken cancellationToken) =>
    {
        try
        {
            var applied = await templates
                .ApplyAsync(userId, companyId, code, request?.Amount ?? 0m, request?.AmountIncludesVat ?? false, cancellationToken)
                .ConfigureAwait(false);
            return applied is null ? Results.NotFound() : Results.Ok(applied);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["lancamentoTipo"] = [exception.Message] });
        }
    });

    // Terceiros a partir do SAF-T: «preview» só analisa, «import» grava.
    foreach (var (route, apply) in new[] { ("parties-preview", false), ("parties-import", true) })
    {
        app.MapPost($"/api/v1/development/users/{{userId:guid}}/companies/{{companyId:guid}}/saft/{route}", async (
            Guid userId, Guid companyId, HttpRequest request, PartyImportService imports, CancellationToken cancellationToken) =>
        {
            var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxBodySize is { IsReadOnly: false })
            {
                maxBodySize.MaxRequestBodySize = null;
            }

            try
            {
                var result = await imports
                    .FromSaftAsync(userId, companyId, request.Body, apply, cancellationToken)
                    .ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (SaftFileFormatException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["saft"] = [exception.Message] });
            }
        });

        app.MapPost($"/api/v1/development/users/{{userId:guid}}/companies/{{companyId:guid}}/efatura/{route}", async (
            Guid userId, Guid companyId, PartyImportService imports, CancellationToken cancellationToken) =>
        {
            var result = await imports.FromEFaturaAsync(userId, companyId, apply, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/vat-summary", async (
        Guid userId, Guid companyId, string? from, string? to,
        VatSummaryService summaries, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var summary = await summaries.BuildAsync(userId, companyId, periodFrom, periodTo, cancellationToken).ConfigureAwait(false);
        return summary is null ? Results.NotFound() : Results.Ok(summary);
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/irc-estimate", async (
        Guid userId, Guid companyId, string? from, string? to, IrcEstimateRequest? request,
        IrcEstimateService irc, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        if (request is null || request.TaxRate <= 0m || request.TaxRate > 1m)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["taxRate"] = ["Indique uma taxa entre 0% e 100%, em fração (ex.: 0.21 para 21%)."]
            });
        }

        var adjustments = (request.Adjustments ?? [])
            .Select(item => new AccountingAI.Application.Irc.IrcAdjustment((item.Description ?? string.Empty).Trim(), item.Amount))
            .ToList();

        var estimate = await irc
            .BuildAsync(userId, companyId, periodFrom, periodTo, request.TaxRate, adjustments, cancellationToken)
            .ConfigureAwait(false);
        return estimate is null ? Results.NotFound() : Results.Ok(estimate);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/irc-estimate/saved", async (
        Guid userId, Guid companyId, string? from, string? to,
        IrcEstimateFilingService filings, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        var view = await filings.GetAsync(userId, companyId, periodFrom, periodTo, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapPut("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/irc-estimate/saved", async (
        Guid userId, Guid companyId, string? from, string? to, IrcEstimateRequest? request,
        IrcEstimateFilingService filings, CancellationToken cancellationToken) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        if (fromDate is not { } periodFrom || toDate is not { } periodTo)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique a data inicial e a data final do período."]
            });
        }

        if (request is null || request.TaxRate <= 0m || request.TaxRate > 1m)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["taxRate"] = ["Indique uma taxa entre 0% e 100%, em fração (ex.: 0.21 para 21%)."]
            });
        }

        var adjustments = (request.Adjustments ?? [])
            .Select(item => new AccountingAI.Application.Irc.IrcAdjustment((item.Description ?? string.Empty).Trim(), item.Amount))
            .ToList();

        var view = await filings
            .SaveAsync(userId, companyId, periodFrom, periodTo, request.TaxRate, adjustments, cancellationToken)
            .ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/irc-estimate/history", async (
        Guid userId, Guid companyId, IrcEstimateFilingService filings, CancellationToken cancellationToken) =>
    {
        var list = await filings.ListAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        return list is null ? Results.NotFound() : Results.Ok(list);
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

    app.MapPost("/api/v1/development/users/{userId:guid}/saft/validate", async (
        Guid userId,
        HttpRequest request,
        AccountingDbContext context,
        ISaftSchemaValidator schemaValidator,
        ISaftValidationModelReader modelReader,
        ISaftAnalyticalValidator analyticalValidator,
        CancellationToken cancellationToken,
        Guid? companyId,
        string? fileName) =>
    {
        var userExists = await context.UserAccounts
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!userExists)
        {
            return Results.NotFound();
        }

        Guid? scopedCompanyId = null;
        if (companyId is { } company &&
            await HasCompanyAccessAsync(context, userId, company, cancellationToken).ConfigureAwait(false))
        {
            scopedCompanyId = company;
        }

        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        // O corpo do pedido não é reposicionável e vão lê-lo duas camadas
        // (esquema e analítica); grava-se num ficheiro temporário uma vez.
        var tempPath = Path.Combine(Path.GetTempPath(), $"saft-validate-endpoint-{Guid.NewGuid():N}.xml");
        try
        {
            await using (var temp = File.Create(tempPath))
            {
                await request.Body.CopyToAsync(temp, cancellationToken).ConfigureAwait(false);
            }

            SaftValidationReport report;
            await using (var schemaStream = File.OpenRead(tempPath))
            {
                report = await schemaValidator.ValidateAsync(schemaStream, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<SaftValidationIssue> analyticalIssues;
            await using (var modelStream = File.OpenRead(tempPath))
            {
                var model = await modelReader.ReadAsync(modelStream, cancellationToken).ConfigureAwait(false);
                analyticalIssues = analyticalValidator.Validate(model);
            }

            var allIssues = report.Issues.Concat(analyticalIssues).ToList();
            var errorCount = allIssues.Count(issue => issue.Severity == SaftValidationSeverity.Error);
            var warningCount = allIssues.Count(issue => issue.Severity == SaftValidationSeverity.Warning);
            var informationCount = allIssues.Count(issue => issue.Severity == SaftValidationSeverity.Information);
            var issuePayload = allIssues.Select(issue => new
            {
                issue.RuleId,
                Severity = issue.Severity.ToString(),
                issue.Scope,
                issue.Message,
                issue.Line,
                issue.Position,
                issue.Path
            }).ToList();
            var issuesJson = JsonSerializer.Serialize(issuePayload);

            context.SaftValidationRuns.Add(SaftValidationRun.Create(
                scopedCompanyId, userId,
                string.IsNullOrWhiteSpace(fileName) ? "(sem nome)" : fileName,
                report.Version, report.SchemaAvailable, report.SchemaValid,
                errorCount, warningCount, informationCount, report.Truncated, issuesJson));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Results.Ok(new
            {
                report.Version,
                report.SchemaAvailable,
                report.SchemaValid,
                Counts = new { Error = errorCount, Warning = warningCount, Information = informationCount },
                Issues = issuePayload,
                report.Truncated
            });
        }
        catch (SaftFileFormatException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["saft"] = [exception.Message]
            });
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // limpeza best-effort
            }
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/validation-reports", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var runs = await context.SaftValidationRuns
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(runs.Select(run => new
        {
            run.Id,
            run.FileName,
            Version = run.AuditFileVersion,
            run.SchemaAvailable,
            run.SchemaValid,
            Counts = new { Error = run.ErrorCount, Warning = run.WarningCount, Information = run.InformationCount },
            run.Truncated,
            run.CreatedAtUtc,
            Issues = JsonSerializer.Deserialize<JsonElement>(run.IssuesJson)
        }));
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/efatura/purchases-import", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        EFaturaPurchaseImportService importService,
        CancellationToken cancellationToken,
        bool? dryRun,
        string? fileName) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await importService
                .ImportAsync(userId, companyId, request.Body,
                    string.IsNullOrWhiteSpace(fileName) ? "(sem nome)" : fileName,
                    dryRun ?? false, cancellationToken)
                .ConfigureAwait(false);

            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (EFaturaCsvFormatException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["efatura"] = [exception.Message]
            });
        }
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/efatura/purchases", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        CancellationToken cancellationToken,
        string? from,
        string? to,
        int? page,
        int? pageSize) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        var query = context.EFaturaPurchases
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId);

        if (fromDate is { } lowerBound)
        {
            query = query.Where(item => item.InvoiceDate >= lowerBound);
        }

        if (toDate is { } upperBound)
        {
            query = query.Where(item => item.InvoiceDate <= upperBound);
        }

        var ordered = query
            .OrderByDescending(item => item.InvoiceDate)
            .ThenBy(item => item.SupplierName);

        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var totalItems = await ordered.CountAsync(cancellationToken).ConfigureAwait(false);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)size));
        var currentPage = Math.Clamp(page ?? 1, 1, totalPages);

        var items = await ordered
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(item => new
            {
                item.Id,
                item.SupplierTaxId,
                item.SupplierName,
                item.InvoiceNo,
                item.Atcud,
                item.InvoiceDate,
                item.InvoiceType,
                item.Status,
                item.Sector,
                item.TaxBase,
                item.TaxAmount,
                item.GrossTotal,
                item.SourceFileName,
                item.CreatedAtUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { Items = items, Page = currentPage, PageSize = size, TotalItems = totalItems, TotalPages = totalPages });
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/cross-check", async (
        Guid userId,
        Guid companyId,
        ICrossCheckService crossCheck,
        CancellationToken cancellationToken,
        string? from,
        string? to) =>
    {
        if (!TryParseReportDateRange(from, to, out var fromDate, out var toDate, out var dateError))
        {
            return dateError!;
        }

        // O cruzamento é sempre sobre um conjunto de meses fechado do exercício.
        if (fromDate is not { } lowerBound || toDate is not { } upperBound)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["Indique as datas de início e de fim do período a cruzar."]
            });
        }

        var report = await crossCheck
            .BuildAsync(userId, companyId, lowerBound, upperBound, cancellationToken)
            .ConfigureAwait(false);

        return report is null ? Results.NotFound() : Results.Ok(report);
    });

    app.MapGet("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/record-info", async (
        Guid userId,
        Guid companyId,
        AccountingDbContext context,
        RecordInfoService recordInfo,
        CancellationToken cancellationToken,
        string? table,
        string? id) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(id))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["parametros"] = ["Indique a tabela (table) e o identificador do registo (id)."]
            });
        }

        var info = await recordInfo
            .FindAsync(table.Trim(), companyId, id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (info is null)
        {
            return Results.NotFound();
        }

        var authorIds = new[] { info.CreatedBy, info.ModifiedBy }
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
        var names = authorIds.Count == 0
            ? []
            : await context.UserAccounts
                .AsNoTracking()
                .Where(account => authorIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, account => account.DisplayName, cancellationToken)
                .ConfigureAwait(false);

        object? Party(Guid? authorId) =>
            authorId is { } value ? new { id = value, name = names.GetValueOrDefault(value) } : null;

        return Results.Ok(new
        {
            table = table.Trim(),
            id = id.Trim(),
            createdBy = Party(info.CreatedBy),
            createdAtUtc = info.CreatedAtUtc,
            modifiedBy = Party(info.ModifiedBy),
            modifiedAtUtc = info.ModifiedAtUtc
        });
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/sales-invoices-summary", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        AccountingDbContext context,
        ISaftSalesInvoicesReader reader,
        CancellationToken cancellationToken) =>
    {
        if (!await HasCompanyAccessAsync(context, userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var summary = await reader
                .ReadSummaryAsync(request.Body, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                summary.HasSalesInvoices,
                summary.DeclaredNumberOfEntries,
                summary.DeclaredTotalDebit,
                summary.DeclaredTotalCredit,
                summary.InvoiceCount,
                summary.VoidedCount,
                summary.Net,
                summary.Tax,
                summary.Gross,
                summary.FromDate,
                summary.ToDate,
                Months = summary.Months.Select(month => new
                {
                    month.Year,
                    month.Month,
                    month.Count,
                    month.Net,
                    month.Tax,
                    month.Gross
                })
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

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/accounts-preview", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        SaftAccountsPreviewService preview,
        CancellationToken cancellationToken) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await preview
                .PreviewAsync(userId, companyId, request.Body, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                result.FileAccountCount,
                result.ExistingCount,
                result.NewCount,
                result.CompanyPostingAccountCount,
                Accounts = result.Accounts.Select(account => new
                {
                    account.Code,
                    account.Description,
                    State = account.State.ToString()
                })
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

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/accounts-import", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        AccountingDbContext context,
        SaftAccountsImportService import,
        CancellationToken cancellationToken,
        int? fiscalYear) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await import
                .ImportAsync(userId, companyId, request.Body, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            if (fiscalYear.HasValue)
            {
                await EnsureDefaultFiscalYearAsync(context, userId, companyId, fiscalYear.Value, cancellationToken).ConfigureAwait(false);
            }

            return Results.Ok(new
            {
                result.CreatedCount,
                result.AlreadyExistedCount,
                Created = result.Created.Select(account => new
                {
                    account.Code,
                    account.Name,
                    account.ParentCode,
                    account.AllowsPosting
                })
            });
        }
        catch (Exception exception) when (exception is SaftFileFormatException or ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["saft"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/opening-balances-import", async (
        Guid userId,
        Guid companyId,
        int fiscalYear,
        HttpRequest request,
        AccountingDbContext context,
        SaftOpeningBalancesImportService import,
        CancellationToken cancellationToken) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await import
                .ImportAsync(userId, companyId, fiscalYear, request.Body, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            await EnsureDefaultFiscalYearAsync(context, userId, companyId, fiscalYear, cancellationToken).ConfigureAwait(false);

            return Results.Ok(new
            {
                result.LineCount,
                result.TotalDebit,
                result.TotalCredit,
                result.EntryReference,
                result.AlreadyImported
            });
        }
        catch (Exception exception) when (exception is SaftFileFormatException or ArgumentException or InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["saft"] = [exception.Message]
            });
        }
    });

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/entries-preview", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        SaftEntriesPreviewService preview,
        CancellationToken cancellationToken) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await preview
                .PreviewAsync(userId, companyId, request.Body, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                result.HasEntries,
                result.TransactionCount,
                result.DeclaredEntryCount,
                result.TotalDebit,
                result.TotalCredit,
                result.Balanced,
                result.FromDate,
                result.ToDate,
                Journals = result.Journals.Select(journal => new
                {
                    journal.Code,
                    journal.Description,
                    journal.TransactionCount,
                    journal.Exists
                })
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

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/entries-analyze", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        SaftEntriesImportService import,
        CancellationToken cancellationToken) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await import
                .AnalyzeAsync(userId, companyId, request.Body, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                result.WouldImportCount,
                result.WouldSkipCount,
                result.JournalsToCreate,
                Skipped = result.Skipped.Select(item => new { item.TransactionId, item.Reason }),
                result.DeclaredEntryCount,
                result.DeclaredTotalDebit,
                result.DeclaredTotalCredit,
                result.FilePeriodFrom,
                result.FilePeriodTo,
                result.ProjectedPeriodDebit,
                result.ProjectedPeriodCredit,
                result.WouldMatchDeclaredTotals
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

    app.MapPost("/api/v1/development/users/{userId:guid}/companies/{companyId:guid}/saft/entries-import", async (
        Guid userId,
        Guid companyId,
        HttpRequest request,
        AccountingDbContext context,
        SaftEntriesImportService import,
        CancellationToken cancellationToken,
        int? fiscalYear) =>
    {
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = null;
        }

        try
        {
            var result = await import
                .ImportAsync(userId, companyId, request.Body, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound();
            }

            if (fiscalYear.HasValue)
            {
                await EnsureDefaultFiscalYearAsync(context, userId, companyId, fiscalYear.Value, cancellationToken).ConfigureAwait(false);
            }

            return Results.Ok(new
            {
                result.ImportedCount,
                result.SkippedCount,
                result.JournalsCreated,
                Skipped = result.Skipped.Select(item => new { item.TransactionId, item.Reason }),
                result.DeclaredEntryCount,
                result.DeclaredTotalDebit,
                result.DeclaredTotalCredit,
                result.FilePeriodFrom,
                result.FilePeriodTo,
                result.CompanyPeriodDebit,
                result.CompanyPeriodCredit,
                result.MatchesDeclaredTotals,
                result.LinesWithParty,
                result.TransactionsWithUnknownParty
            });
        }
        catch (Exception exception) when (exception is SaftFileFormatException or ArgumentException or InvalidOperationException)
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

            if (request.FiscalYear.HasValue)
            {
                await EnsureDefaultFiscalYearAsync(context, userId, created.CompanyId, request.FiscalYear.Value, cancellationToken).ConfigureAwait(false);
            }

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
                    cancellationToken,
                    associateUserId: DevelopmentDataSeeder.DemoUserId)
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
                .UpdateCompanyAsync(
                    companyId, request.LegalName, region, request.BaseCurrency, request.IsActive, postingLockDate, cancellationToken,
                    request.AddressDetail, request.City, request.PostalCode)
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

// Serve o `index.html` da PWA para qualquer pedido que não caia em nenhuma
// API nem em nenhum ficheiro estático — o encaminhamento das rotas da SPA
// (`/movimentos`, etc.) é do `react-router-dom`, do lado do cliente. Aberto
// a quem não tem sessão: é a própria página de login. Sem `wwwroot/index.html`
// (nenhum `npm run build` publicado ainda), devolve 404 em vez de rebentar.
app.MapFallbackToFile("index.html").AllowAnonymous();

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
        movement.DocumentDate,
        movement.Description,
        movement.Notes,
        movement.Status,
        Source = movement.Source.ToString(),
        movement.Amount,
        Lines = movement.Lines.Select(line => new
        {
            Account = line.AccountCode,
            line.AccountName,
            line.Description,
            line.Debit,
            line.Credit,
            line.PartyCountryCode,
            line.PartyTaxNumber,
            line.PartyName,
            line.VatCode,
            line.CostCenter
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

    private static bool TryParseVatClassification(
        string? rate, string? nature, string? region,
        out AccountingAI.Domain.Accounting.VatRate parsedRate,
        out AccountingAI.Domain.Accounting.VatOperationNature parsedNature,
        out AccountingAI.Domain.Accounting.VatRegion parsedRegion,
        out IResult? error)
    {
        parsedRate = AccountingAI.Domain.Accounting.VatRate.Nenhuma;
        parsedNature = AccountingAI.Domain.Accounting.VatOperationNature.Nenhuma;
        parsedRegion = AccountingAI.Domain.Accounting.VatRegion.Continente;
        error = null;

        if (!string.IsNullOrWhiteSpace(rate) && !Enum.TryParse(rate.Trim(), ignoreCase: true, out parsedRate))
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]> { ["taxa"] = ["Taxa de IVA inválida."] });
            return false;
        }

        if (!string.IsNullOrWhiteSpace(nature) && !Enum.TryParse(nature.Trim(), ignoreCase: true, out parsedNature))
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]> { ["natureza"] = ["Natureza da operação inválida."] });
            return false;
        }

        if (!string.IsNullOrWhiteSpace(region) && !Enum.TryParse(region.Trim(), ignoreCase: true, out parsedRegion))
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]> { ["regiao"] = ["Região inválida."] });
            return false;
        }

        return true;
    }

    private static bool TryParseAtSettings(
        string? periodicity, string? environment,
        out AccountingAI.Domain.Organizations.VatFilingPeriodicity parsedPeriodicity,
        out AccountingAI.Domain.Organizations.AtEnvironment parsedEnvironment,
        out IResult? error)
    {
        parsedPeriodicity = AccountingAI.Domain.Organizations.VatFilingPeriodicity.Mensal;
        parsedEnvironment = AccountingAI.Domain.Organizations.AtEnvironment.Testes;
        error = null;

        if (!string.IsNullOrWhiteSpace(periodicity) && !Enum.TryParse(periodicity.Trim(), ignoreCase: true, out parsedPeriodicity))
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]> { ["periodicidade"] = ["Periodicidade inválida — use Mensal ou Trimestral."] });
            return false;
        }

        if (!string.IsNullOrWhiteSpace(environment) && !Enum.TryParse(environment.Trim(), ignoreCase: true, out parsedEnvironment))
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]> { ["ambiente"] = ["Ambiente inválido — use Testes ou Producao."] });
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

    private static async Task<SaveAnalysisRequest?> ReadAnalysisRequestAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await request
                .ReadFromJsonAsync<SaveAnalysisRequest>(
                    AccountingAI.Application.Reporting.AnalysisJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> RunAnalysisAsync(
        IAnalysisRunner runner,
        Guid userId,
        Guid companyId,
        AccountingAI.Application.Reporting.AnalysisDefinition definition,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner
                .RunAsync(userId, companyId, definition, from, to, cancellationToken)
                .ConfigureAwait(false);
            return result is null
                ? Results.NotFound()
                : Results.Json(result, AccountingAI.Application.Reporting.AnalysisJson.Options);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["analise"] = [exception.Message] });
        }
    }

    private static IResult AnalysisWriteResult(SavedAnalysisWriteResult result)
    {
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        if (result.Errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["analise"] = [.. result.Errors] });
        }

        return Results.Json(result.Saved, AccountingAI.Application.Reporting.AnalysisJson.Options);
    }

    // Depois de importar um SAF-T, o exercício do ficheiro passa a ser o exercício do
    // utilizador nesta empresa — sem isto, os filtros de período (Resultados, Balancete,
    // etc.) caem no ano corrente por não haver nenhum exercício definido, mesmo já
    // existindo dados do ano importado. Não falha o pedido de importação por causa disto:
    // é um acerto de preferência, não uma parte essencial da própria importação.
    private static async Task EnsureDefaultFiscalYearAsync(
        AccountingDbContext context,
        Guid userId,
        Guid companyId,
        int fiscalYear,
        CancellationToken cancellationToken)
    {
        // Ano fora do intervalo aceite (um SAF-T real nunca lá chega): ignora-se em
        // silêncio, é um acerto de preferência e não deve fazer falhar a importação.
        if (fiscalYear is < 2000 or > 2200)
        {
            return;
        }

        var membership = await context.CompanyUsers
            .Include(item => item.Settings)
            .SingleOrDefaultAsync(item => item.UserId == userId && item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (membership is null)
        {
            return;
        }

        var settings = membership.Settings ?? new UserCompanySettings(companyId, userId);
        settings.SetDefaultFiscalYear(fiscalYear);
        if (membership.Settings is null)
        {
            context.UserCompanySettings.Add(settings);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

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
            TextSize = settings.TextSize.ToString(),
            settings.UpdatedAtUtc
        };

    // Preferências por omissão para uma empresa a que o utilizador está associado
    // mas ainda não personalizou — evita um objeto nulo no contexto de trabalho.
    private static object DefaultSettingsResponse() => new
    {
        Culture = "pt-PT",
        TimeZone = "Europe/Lisbon",
        DefaultFiscalYear = (int?)null,
        Theme = InterfaceTheme.System.ToString(),
        CompactMode = false,
        PageSize = 50,
        TextSize = InterfaceTextSize.Normal.ToString(),
        UpdatedAtUtc = (DateTimeOffset?)null
    };
}

public sealed record UpdateUserCompanySettingsRequest(
    string Culture,
    string TimeZone,
    int? DefaultFiscalYear,
    string Theme,
    bool CompactMode,
    int PageSize,
    string TextSize = "Normal");

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

public sealed record SaveAnalysisRequest(
    string? Name,
    string? Description,
    AccountingAI.Application.Reporting.AnalysisDefinition? Definition);

public sealed record PutReportTemplateRequest(
    string[]? HeaderLines,
    bool ShowFirmName,
    bool ShowCompanyIdentity,
    string? Orientation);

public sealed record PutAiSettingsRequest(bool Enabled, bool AutoMonthly);

public sealed record GenerateManagementReportRequest(string? Kind, string? Question);

public sealed record SaveVatReturnRequest(decimal PriorPeriodCredit);

public sealed record PutAtCredentialsRequest(string? SubUser, string? Password, string? Periodicity, string? Environment);

public sealed record PutVatClassificationRequest(string? Rate, string? Nature, string? Region);

public sealed record BulkVatClassificationRequest(string? Prefix, string? Rate, string? Nature, string? Region);

public sealed record EntryTemplateLineRequest(string? Kind, string? Side, string? AccountCode, string? VatCode = null, string? Description = null);

public sealed record EntryTemplateRequest(
    string? Name,
    string? JournalCode,
    string? DocumentTypeCode,
    string? Description,
    bool IsActive,
    List<EntryTemplateLineRequest>? Lines);

public sealed record ApplyEntryTemplateRequest(decimal Amount, bool AmountIncludesVat = false);

public sealed record CostCenterRequest(string? Code, string? Name, bool IsActive = true);

public sealed record VatCodeRequest(
    string? Code,
    string? Name,
    string? Rate,
    decimal Percentage,
    string? Nature,
    string? Region,
    bool IsActive = true);

public sealed record CreatePartyRequest(
    string? CountryCode,
    string? TaxRegistrationNumber,
    string? Name,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerAccountCode = null,
    string? SupplierAccountCode = null,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

public sealed record UpdatePartyRequest(
    string? Name,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerAccountCode = null,
    string? SupplierAccountCode = null,
    bool IsActive = true,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

public sealed record IrcAdjustmentRequest(string? Description, decimal Amount);

public sealed record IrcEstimateRequest(decimal TaxRate, List<IrcAdjustmentRequest>? Adjustments);

public sealed record CreateSaftCompanyRequest(
    string TaxRegistrationNumber,
    string LegalName,
    string TaxRegion,
    string AccountingStandard,
    string? BaseCurrency,
    int? FiscalYear = null);

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
    string? PostingLockDate = null,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

public sealed record CreateOrganizationUserRequest(string DisplayName, string Email);

public sealed record UpdateOrganizationUserRequest(string DisplayName, string Email, bool IsActive);

public sealed record SetMembershipRequest(string Role, bool IsActive);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record MovementLineRequest(
    string AccountCode,
    string Description,
    decimal Debit,
    decimal Credit,
    string? PartyTaxNumber = null,
    string? PartyCountryCode = null,
    string? VatCode = null,
    string? CostCenter = null);

public sealed record CreateMovementRequest(
    string JournalCode,
    DateOnly EntryDate,
    string DocumentReference,
    string Description,
    bool Reviewed,
    List<MovementLineRequest> Lines,
    string? DocumentTypeCode = null,
    string? Notes = null,
    DateOnly? DocumentDate = null);
