using NexGenAccountingAI.Application.Auditing;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma tabela que o «Sobre o Registo» sabe consultar.</summary>
public sealed record RecordInfoTable(string Schema, string Name, bool CompanyScoped);

/// <summary>
/// Resolve os carimbos de auditoria (<see cref="RecordAuditInfo"/>) de um registo
/// identificado por um nome lógico de tabela e uma chave (o código, para as
/// tabelas com código; o <c>Id</c>, para as restantes). As entidades sem
/// colunas-sombra completas (<c>EFaturaPurchase</c>, <c>SaftValidationRun</c>)
/// devolvem só a data de criação.
/// </summary>
public sealed class RecordInfoService(AccountingDbContext context)
{
    private delegate Task<RecordAuditInfo?> Resolver(
        AccountingDbContext context, Guid companyId, string id, CancellationToken cancellationToken);

    private static readonly Dictionary<string, (RecordInfoTable Table, Resolver Resolve)> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["movimentos"] = (new("accounting", "JournalEntries", true),
                (c, company, id, ct) => Audited(c.JournalEntries.Where(x => x.CompanyId == company && x.Reference == Code(id)), ct)),
            ["contas"] = (new("accounting", "Accounts", true),
                (c, company, id, ct) => Audited(c.Accounts.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["diarios"] = (new("accounting", "Journals", true),
                (c, company, id, ct) => Audited(c.Journals.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["documentos"] = (new("accounting", "AccountingDocumentTypes", true),
                (c, company, id, ct) => Audited(c.AccountingDocumentTypes.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["series"] = (new("accounting", "DocumentNumberSeries", true),
                (c, company, id, ct) => ById(id, key => Audited(c.DocumentNumberSeries.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["empresas"] = (new("organization", "Companies", false),
                (c, _, id, ct) => ById(id, key => Audited(c.Companies.Where(x => x.Id == key), ct))),
            ["utilizadores"] = (new("identity", "UserAccounts", false),
                (c, _, id, ct) => ById(id, key => Audited(c.UserAccounts.Where(x => x.Id == key), ct))),
            ["analises"] = (new("reporting", "SavedAnalysis", true),
                (c, company, id, ct) => ById(id, key => Audited(c.SavedAnalyses.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["relatorios-gestao"] = (new("reporting", "ManagementReport", true),
                (c, company, id, ct) => ById(id, key => Audited(c.ManagementReports.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["apuramentos-iva"] = (new("accounting", "VatReturnFiling", true),
                (c, company, id, ct) => ById(id, key => Audited(c.VatReturnFilings.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["credenciais-at"] = (new("organization", "CompanyAtCredentials", true),
                (c, company, id, ct) => ById(id, key => Audited(c.CompanyAtCredentials.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["classificacoes-iva"] = (new("accounting", "AccountVatClassification", true),
                (c, company, id, ct) => ById(id, key => Audited(c.AccountVatClassifications.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["estimativas-irc"] = (new("accounting", "IrcEstimateFiling", true),
                (c, company, id, ct) => ById(id, key => Audited(c.IrcEstimateFilings.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["terceiros"] = (new("accounting", "Parties", true),
                (c, company, id, ct) => ById(id, key => Audited(c.Parties.Where(x => x.CompanyId == company && x.Id == key), ct))),
            ["centros-custo"] = (new("accounting", "CostCenters", true),
                (c, company, id, ct) => Audited(c.CostCenters.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["ativos-fixos"] = (new("accounting", "FixedAssets", true),
                (c, company, id, ct) => Audited(c.FixedAssets.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["lancamentos-tipo"] = (new("accounting", "EntryTemplates", true),
                (c, company, id, ct) => Audited(c.EntryTemplates.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["codigos-iva"] = (new("accounting", "VatCodes", true),
                (c, company, id, ct) => Audited(c.VatCodes.Where(x => x.CompanyId == company && x.Code == Code(id)), ct)),
            ["compras-efatura"] = (new("accounting", "EFaturaPurchase", true),
                (c, company, id, ct) => ById(id, key => CreatedOnly(
                    c.EFaturaPurchases.Where(x => x.CompanyId == company && x.Id == key).Select(x => (DateTimeOffset?)x.CreatedAtUtc), ct))),
            ["validacoes-saft"] = (new("accounting", "SaftValidationRun", true),
                (c, company, id, ct) => ById(id, key => CreatedOnly(
                    c.SaftValidationRuns.Where(x => x.CompanyId == company && x.Id == key).Select(x => (DateTimeOffset?)x.CreatedAtUtc), ct))),
        };

    public static bool Knows(string table) => Registry.ContainsKey(table);

    public static IReadOnlyDictionary<string, RecordInfoTable> Tables =>
        Registry.ToDictionary(pair => pair.Key, pair => pair.Value.Table, StringComparer.OrdinalIgnoreCase);

    /// <summary>`true` quando a tabela é âmbito de empresa (o acesso deve ser verificado antes).</summary>
    public static bool IsCompanyScoped(string table) =>
        Registry.TryGetValue(table, out var entry) && entry.Table.CompanyScoped;

    public Task<RecordAuditInfo?> FindAsync(
        string table, Guid companyId, string id, CancellationToken cancellationToken = default) =>
        Registry.TryGetValue(table, out var entry)
            ? entry.Resolve(context, companyId, id, cancellationToken)
            : Task.FromResult<RecordAuditInfo?>(null);

    private static string Code(string id) => id.Trim().ToUpperInvariant();

    private static Task<RecordAuditInfo?> ById(string id, Func<Guid, Task<RecordAuditInfo?>> resolve) =>
        Guid.TryParse(id, out var key) ? resolve(key) : Task.FromResult<RecordAuditInfo?>(null);

    private static async Task<RecordAuditInfo?> Audited<TEntity>(
        IQueryable<TEntity> source, CancellationToken cancellationToken) where TEntity : class =>
        await source
            .Select(entity => new RecordAuditInfo(
                EF.Property<DateTimeOffset?>(entity, "CreatedAtUtc"),
                EF.Property<Guid?>(entity, "CreatedBy"),
                EF.Property<DateTimeOffset?>(entity, "ModifiedAtUtc"),
                EF.Property<Guid?>(entity, "ModifiedBy")))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static async Task<RecordAuditInfo?> CreatedOnly(
        IQueryable<DateTimeOffset?> createdAt, CancellationToken cancellationToken)
    {
        var found = await createdAt.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return found is null ? null : new RecordAuditInfo(found, null, null, null);
    }
}
