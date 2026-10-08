using System.Text.RegularExpressions;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence;

namespace NexGenAccountingAI.Api;

/// <summary>
/// A tabela que diz que permissão exige cada endpoint de uma empresa (rotas com <c>{userId}</c> e
/// <c>{companyId}</c>). O caminho é o que vem depois de <c>.../companies/{companyId}/</c>, com os
/// parâmetros reduzidos a <c>{}</c>. Um endpoint sem regra é recusado por omissão, e um teste confere
/// que todos os endpoints da API têm uma (ADR-0018).
/// </summary>
public static partial class CompanyPermissionRules
{
    private static readonly Dictionary<(string Method, string Path), string> Table = Parse("""
        GET accounting/accounts/{}/ledger = reports.view
        GET accounting/balancete = reports.view
        GET accounting/trial-balance = reports.view
        GET accounting/journal-report = reports.view
        GET accounting/journal-movement-counts = reports.view
        GET accounting/closed-years = yearend.view
        GET accounting/year-end-closing = yearend.view
        GET accounting/year-opening = yearend.view
        POST accounting/year-end-closing = yearend.execute
        POST accounting/year-opening = yearend.execute
        POST accounting/year-end-closing/reopen = yearend.reopen
        POST accounting/year-opening/reopen = yearend.reopen
        GET accounting/setup = accounts.view
        GET accounting/taxonomy-review = accounts.view
        POST accounting/accounts = accounts.create
        PUT accounting/accounts/{} = accounts.edit
        POST accounting/accounts/apply-taxonomy-suggestions = accounts.edit
        POST accounting/documents = documents.create
        PUT accounting/documents/{} = documents.edit
        POST accounting/journals = journals.create
        PUT accounting/journals/{} = journals.edit
        GET accounting/number-series = series.view
        PUT accounting/number-series/{}/{} = series.edit
        GET accounting/posting-lock-date = movements.view
        GET accounting/movements = movements.view
        GET accounting/movements/summary = movements.view
        GET accounting/movements/{} = movements.view
        GET accounting/movements/{}/history = movements.view
        POST accounting/movements = movements.create
        PUT accounting/movements/{} = movements.edit
        POST accounting/movements/{}/review = movements.review
        POST accounting/movements/{}/void = movements.void
        POST accounting/movements/{}/reopen = movements.reopen
        POST invoice-qr/lookup = movements.view
        GET ai-settings = ai.use
        PUT ai-settings = ai.settings
        GET chat = ai.use
        POST chat = ai.use
        GET chat/{} = ai.use
        DELETE chat/{} = ai.use
        POST listings/ai-draft = ai.use
        POST reports/pack/narrate = ai.use
        POST variance/explain = ai.use
        POST suggested-entries/classification/ai = ai.use
        POST management-reports/generate = ai.use
        GET analyses = management.view
        GET analyses/{}/run = management.view
        POST analyses/run = management.view
        POST analyses = management.edit
        PUT analyses/{} = management.edit
        DELETE analyses/{} = management.edit
        GET anomalies = management.view
        POST anomalies/dismiss = management.edit
        POST anomalies/restore = management.edit
        GET management-reports = management.view
        GET management-reports/dossier = management.view
        GET management-reports/{} = management.view
        GET management/monthly-evolution = management.view
        GET management/operating-map = management.view
        GET management/owner-report = management.view
        GET management/ratios = management.view
        GET management/structure = management.view
        GET management/treasury-forecast = management.view
        GET variance = management.view
        GET budget = budget.view
        PUT budget = budget.edit
        POST budget/copy = budget.edit
        GET closing = closing.view
        PUT closing/{} = closing.edit
        GET at-credentials = at.credentials
        PUT at-credentials = at.credentials
        DELETE at-credentials = at.credentials
        GET at/exchanges = at.view
        POST vat-declaration/at/{} = at.send_vat
        POST ies/at/{} = at.send_ies
        POST modelo-22/at/{} = at.send_modelo22
        POST efatura/purchases-at-import = at.receive_efatura
        GET efatura/purchases = efatura.view
        POST efatura/purchases-import = efatura.import
        GET attachments = archive.view
        GET attachments/{}/content = archive.view
        POST attachments = archive.create
        PUT attachments/{} = archive.edit
        DELETE attachments/{} = archive.delete
        GET bank-accounts = banks.view
        GET bank-entry-rules = banks.view
        GET bank-statements/lines = banks.view
        GET bank-statements/reconciliation = banks.view
        GET bank-statements/entry-suggestions = banks.view
        PUT bank-entry-rules = banks.post
        DELETE bank-entry-rules = banks.post
        POST bank-statements/import = banks.import
        POST bank-statements/auto-reconcile = banks.reconcile
        POST bank-statements/reconciliations = banks.reconcile
        PUT bank-statements/lines/{}/reconciliation = banks.reconcile
        DELETE bank-statements/lines/{}/reconciliation = banks.reconcile
        POST bank-statements/post = banks.post
        GET cost-centers = costcenters.view
        GET cost-centers/report = costcenters.view
        POST cost-centers = costcenters.create
        PUT cost-centers/{} = costcenters.edit
        GET cross-check = reports.view
        GET entry-templates = templates.view
        POST entry-templates/{}/apply = templates.view
        PUT entry-templates/{} = templates.edit
        POST exports/table = *
        POST email/send = reports.export
        GET portal/users = portal.view
        GET portal/items = portal.view
        GET portal/uploads = portal.view
        POST portal/users = portal.publish
        POST portal/users/{}/resend = portal.publish
        DELETE portal/users/{} = portal.publish
        POST portal/items/export = portal.publish
        POST portal/items/attachment = portal.publish
        DELETE portal/items/{} = portal.publish
        POST listings/export = *
        GET fiscal-dossier = dossier.view
        GET fiscal-dossier/maps = dossier.view
        GET fiscal-dossier/maps/xml = dossier.view
        GET fiscal-dossier/modelo30 = dossier.view
        PUT fiscal-dossier/modelo30/{} = dossier.edit
        PUT fiscal-dossier/{} = dossier.edit
        GET fixed-assets = fixedassets.view
        GET fixed-assets/schedule = fixedassets.view
        POST fixed-assets/{}/disposal/preview = fixedassets.view
        POST fixed-assets = fixedassets.create
        PUT fixed-assets/{} = fixedassets.edit
        PUT fixed-assets/{}/monetary-coefficient = fixedassets.edit
        POST fixed-assets/schedule/post = fixedassets.post
        POST fixed-assets/{}/disposal = fixedassets.post
        GET ies/annex-a = irc.view
        GET ies/xml = irc.view
        POST irc-estimate = irc.view
        GET irc-estimate/history = irc.view
        GET irc-estimate/saved = irc.view
        PUT irc-estimate/saved = irc.edit
        GET modelo-22 = irc.view
        GET modelo-22/fields = irc.view
        GET modelo-22/xml = irc.view
        PUT modelo-22 = irc.edit
        GET listings = custom.view
        GET listings/catalog = custom.view
        GET listings/templates = custom.view
        POST listings/pivot = custom.view
        POST listings/run = custom.view
        POST listings/{}/favorite = custom.view
        POST listings = custom.edit
        PUT listings/{} = custom.edit
        DELETE listings/{} = custom.edit
        GET maps = custom.view
        GET maps/templates/{} = custom.view
        POST maps/run = custom.view
        POST maps = custom.edit
        PUT maps/{} = custom.edit
        DELETE maps/{} = custom.edit
        GET obligations = obligations.view
        PUT obligations/{}/{} = obligations.edit
        GET parties = parties.view
        GET parties/{}/statement = parties.view
        GET parties/aging = reports.view
        GET parties/balances = reports.view
        POST parties = parties.create
        PUT parties/{} = parties.edit
        POST saft/parties-preview = parties.create
        POST saft/parties-import = parties.create
        POST saft/parties-link-preview = parties.edit
        POST saft/parties-link = parties.edit
        POST efatura/parties-preview = parties.create
        POST efatura/parties-import = parties.create
        GET recapitulative-statement = vat.view
        GET record-info = *
        GET report-templates/{} = reports.view
        PUT report-templates/{} = *
        POST reports/pack = reports.export
        GET reports/pack/presets = reports.view
        GET statements/balance-sheet = reports.view
        GET statements/cash-flows = reports.view
        GET statements/equity-changes = reports.view
        GET statements/income-statement = reports.view
        POST saft/accounts-import = saftimport.import
        POST saft/accounts-preview = saftimport.import
        POST saft/entries-analyze = saftimport.import
        POST saft/entries-import = saftimport.import
        POST saft/entries-preview = saftimport.import
        POST saft/opening-balances-import = saftimport.import
        POST saft/sales-invoices-summary = saftimport.import
        POST sales-documents/import = saftimport.import
        GET saft/export = saftexport.export
        GET saft/export-report = saftexport.export
        GET saft/validation-reports = reports.view
        GET sales-entry-rules = suggested.view
        GET supplier-entry-rules = suggested.view
        GET sales-suggested-entries = suggested.view
        GET suggested-entries = suggested.view
        GET suggested-entries/classification/ai-status = suggested.view
        POST suggested-entries/classification/history = suggested.view
        PUT sales-entry-rules/{} = suggested.post
        DELETE sales-entry-rules/{} = suggested.post
        PUT supplier-entry-rules/{} = suggested.post
        DELETE supplier-entry-rules/{} = suggested.post
        POST sales-suggested-entries/finalize = suggested.post
        POST suggested-entries/classification/accept = suggested.post
        POST suggested-entries/dismiss = suggested.post
        POST suggested-entries/finalize = suggested.post
        POST suggested-entries/restore = suggested.post
        PUT settings = *
        GET tasks/assignees = tasks.view
        POST tasks = tasks.edit
        POST tasks/from-obligations = tasks.edit
        PUT tasks/{} = tasks.edit
        GET tasks/{}/comments = tasks.view
        POST tasks/{}/comments = tasks.edit
        DELETE tasks/{} = tasks.edit
        GET vat-classification = vatconfig.view
        POST vat-classification/bulk = vatconfig.edit
        PUT vat-classification/{} = vatconfig.edit
        GET vat-codes = vatconfig.view
        POST vat-codes = vatconfig.edit
        POST vat-codes/defaults = vatconfig.edit
        PUT vat-codes/{} = vatconfig.edit
        GET vat-declaration = vat.view
        GET vat-declaration/inputs = vat.view
        GET vat-declaration/xml = vat.view
        GET vat-return = vat.view
        GET vat-return/history = vat.view
        GET vat-return/saved = vat.view
        GET vat-summary = vat.view
        GET vat-settlement-entry = vat.view
        PUT vat-declaration/inputs = vat.edit
        PUT vat-return/saved = vat.edit
        POST vat-settlement-entry = vat.post
        GET withholdings = withholdings.view
        """);

    [GeneratedRegex(@"^/api/v1/development/(?:users|work-context)/\{userId[^}]*\}/companies/\{companyId[^}]*\}/(?<path>.*)$")]
    private static partial Regex CompanyRoute();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Parameter();

    /// <summary>O caminho normalizado de uma rota de empresa, ou null se a rota não é de uma empresa.</summary>
    public static string? NormalizedPath(string? routePattern)
    {
        if (routePattern is null)
        {
            return null;
        }

        var match = CompanyRoute().Match(routePattern);
        return match.Success ? Parameter().Replace(match.Groups["path"].Value, "{}") : null;
    }

    /// <summary>A permissão exigida (ou <see cref="PermissionCatalog.AnyMember"/>), ou null se não há regra.</summary>
    public static string? Resolve(string method, string normalizedPath) =>
        Table.TryGetValue((method.ToUpperInvariant(), normalizedPath), out var permission) ? permission : null;

    public static IEnumerable<string> PermissionsUsed => Table.Values.Distinct();

    private static Dictionary<(string, string), string> Parse(string text)
    {
        var table = new Dictionary<(string, string), string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sides = line.Split('=', 2, StringSplitOptions.TrimEntries);
            var left = sides[0].Split(' ', 2, StringSplitOptions.TrimEntries);
            table.Add((left[0], left[1]), sides[1]);
        }

        return table;
    }
}

/// <summary>
/// Cada pedido a uma empresa (rota com <c>{userId}</c> e <c>{companyId}</c>) exige a permissão que a
/// tabela <see cref="CompanyPermissionRules"/> indica, dada pelo grupo do utilizador nessa empresa.
/// Corre depois do <see cref="CurrentUserMiddleware"/>, que já garantiu que o <c>userId</c> é o da
/// sessão. Sem acesso à empresa, o pedido segue e o endpoint responde 404 como sempre. Ver ADR-0018.
/// </summary>
public sealed class CompanyPermissionMiddleware(RequestDelegate next)
{
    public const string ReadOnlyMessage = "Não tem permissão para esta operação nesta empresa. Peça ao dono ou a um administrador outro grupo de permissões.";

    public const string AccountantMessage = "Esta operação usa a senha da AT do contabilista: só um utilizador marcado como contabilista a pode fazer.";

    public async Task InvokeAsync(HttpContext context, PermissionService permissions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permissions);

        if (HttpMethods.IsOptions(context.Request.Method) ||
            context.GetRouteValue("userId") is not string rawUser || !Guid.TryParse(rawUser, out var userId) ||
            context.GetRouteValue("companyId") is not string rawCompany || !Guid.TryParse(rawCompany, out var companyId) ||
            CompanyPermissionRules.NormalizedPath((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText) is not { } path)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var effective = await permissions.ResolveAsync(userId, companyId, context.RequestAborted).ConfigureAwait(false);
        if (!effective.IsMember)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var required = CompanyPermissionRules.Resolve(HttpMethods.IsHead(context.Request.Method) ? HttpMethods.Get : context.Request.Method, path);
        if (required is null || !effective.Has(required))
        {
            var message = required is not null && PermissionCatalog.IsAccountantOnly(required) ? AccountantMessage : ReadOnlyMessage;
            await Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["access"] = [message] },
                    statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        context.Items[EffectivePermissionsKey] = effective;
        await next(context).ConfigureAwait(false);
    }

    public const string EffectivePermissionsKey = "effective-permissions";
}
