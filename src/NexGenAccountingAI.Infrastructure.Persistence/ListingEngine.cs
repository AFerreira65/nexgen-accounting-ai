using System.Linq.Expressions;
using NexGenAccountingAI.Application.Listings;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Corre as listagens do utilizador (F4D-01/02). Cada origem de dados diz ao núcleo
/// (<see cref="ListingRunner{TRow}"/>) o que os seus campos são em SQL; o resto — filtros «e»/«ou»,
/// ordenação, agrupamento por níveis, parâmetros, paginação e totais — é comum. A definição é só
/// dados: o servidor traduz-a para expressões LINQ tipadas, que o EF converte em SQL com parâmetros,
/// e o filtro de empresa vem sempre do chamador (a sessão), nunca da definição.
/// </summary>
public sealed class ListingEngine(AccountingDbContext context)
{
    public const int MaxPageSize = 1000;

    /// <summary>O máximo de linhas numa exportação.</summary>
    public const int MaxExportRows = 100_000;

    /// <summary>Corre a definição para a empresa (já verificado o acesso).</summary>
    /// <exception cref="ArgumentException">A definição ou o pedido não são válidos (mensagem em português).</exception>
    public Task<ListingResult> RunAsync(
        Guid companyId, ListingDefinition definition, ListingRequest request, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);
        return RunnerFor(definition).RunAsync(companyId, definition, request, today, cancellationToken);
    }

    /// <summary>Todas as linhas da definição, sem agrupar (para exportar), até <see cref="MaxExportRows"/>.</summary>
    public Task<(IReadOnlyList<ListingColumn> Columns, IReadOnlyList<ListingRow> Rows, bool Truncated)> ExportAsync(
        Guid companyId, ListingDefinition definition, IReadOnlyDictionary<string, string>? parameters, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return RunnerFor(definition).ExportAsync(companyId, definition, parameters, today, cancellationToken);
    }

    private IListingRunner RunnerFor(ListingDefinition definition)
    {
        var problems = ListingCatalog.Validate(definition);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(definition));
        }

        return definition.Source switch
        {
            ListingCatalog.JournalLines => new ListingRunner<LineRow>(Lines()),
            ListingCatalog.Balances => new ListingRunner<BalanceRow>(Balances()),
            ListingCatalog.Sales => new ListingRunner<SalesRow>(Sales()),
            ListingCatalog.Purchases => new ListingRunner<PurchaseRow>(Purchases()),
            ListingCatalog.Assets => new ListingRunner<AssetRow>(Assets()),
            ListingCatalog.Bank => new ListingRunner<BankRow>(Bank()),
            ListingCatalog.Archive => new ListingRunner<ArchiveRow>(Archive()),
            _ => throw new ArgumentException($"A origem «{definition.Source}» não existe.", nameof(definition)),
        };
    }

    private static Expression<Func<T, TValue>> Value<T, TValue>(Expression<Func<T, TValue>> expression) => expression;

    private static Action<Dictionary<string, decimal>> Balance(string debit, string credit, string result) =>
        sums => sums[result] = sums[debit] - sums[credit];

    // ---- Linhas de lançamentos ----

    /// <summary>A linha plana sobre que a origem «linhas de lançamentos» trabalha.</summary>
    internal sealed class LineRow
    {
        public Guid Id { get; set; }

        public int Sequence { get; set; }

        public DateOnly Date { get; set; }

        public string Reference { get; set; } = string.Empty;

        public string JournalCode { get; set; } = string.Empty;

        public string DocumentReference { get; set; } = string.Empty;

        public JournalEntryStatus Status { get; set; }

        public string AccountCode { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public Guid? PartyId { get; set; }

        public string? PartyName { get; set; }

        public string? PartyTax { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? CostCenter { get; set; }

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }
    }

    private ListingBinding<LineRow> Lines() => new()
    {
        Base = companyId => context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId)
            .Select(line => new LineRow
            {
                Id = line.Id,
                Sequence = line.Sequence,
                Date = line.JournalEntry.EntryDate,
                Reference = line.JournalEntry.Reference,
                JournalCode = line.JournalEntry.Journal.Code,
                DocumentReference = line.JournalEntry.DocumentReference,
                Status = line.JournalEntry.Status,
                AccountCode = line.Account.Code,
                AccountName = line.Account.Name,
                PartyId = line.PartyId,
                PartyName = line.Party == null ? null : line.Party.Name,
                PartyTax = line.Party == null ? null : line.Party.TaxRegistrationNumber,
                Description = line.Description,
                CostCenter = line.CostCenter == null ? null : line.CostCenter.Name,
                Debit = line.Debit,
                Credit = line.Credit,
            }),
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["data"] = Value<LineRow, DateOnly>(row => row.Date),
            ["lancamento"] = Value<LineRow, string>(row => row.Reference),
            ["diario"] = Value<LineRow, string>(row => row.JournalCode),
            ["documento"] = Value<LineRow, string>(row => row.DocumentReference),
            ["estado"] = Value<LineRow, string>(row => row.Status == JournalEntryStatus.Voided ? "Anulado" : row.Status == JournalEntryStatus.Reviewed ? "Revisto" : "Por rever"),
            ["conta"] = Value<LineRow, string>(row => row.AccountCode),
            ["nomeConta"] = Value<LineRow, string>(row => row.AccountName),
            ["classe"] = Value<LineRow, string>(row => row.AccountCode.Substring(0, 1)),
            ["terceiro"] = Value<LineRow, string?>(row => row.PartyName),
            ["nif"] = Value<LineRow, string?>(row => row.PartyTax),
            ["descricao"] = Value<LineRow, string>(row => row.Description),
            ["centroCusto"] = Value<LineRow, string?>(row => row.CostCenter),
            ["ano"] = Value<LineRow, int>(row => row.Date.Year),
            ["mes"] = Value<LineRow, int>(row => row.Date.Month),
            ["debito"] = Value<LineRow, decimal>(row => row.Debit),
            ["credito"] = Value<LineRow, decimal>(row => row.Credit),
            ["saldo"] = Value<LineRow, decimal>(row => row.Debit - row.Credit),
        },
        GroupKeys = new Dictionary<string, Expression<Func<LineRow, string?>>>(StringComparer.Ordinal)
        {
            ["diario"] = row => row.JournalCode,
            ["estado"] = row => row.Status == JournalEntryStatus.Voided ? "Anulado" : row.Status == JournalEntryStatus.Reviewed ? "Revisto" : "Por rever",
            ["conta"] = row => row.AccountCode,
            ["nomeConta"] = row => row.AccountName,
            ["classe"] = row => row.AccountCode.Substring(0, 1),
            ["terceiro"] = row => row.PartyName,
            ["centroCusto"] = row => row.CostCenter,
            ["ano"] = row => row.Date.Year.ToString(),
            ["mes"] = row => row.Date.Month.ToString(),
        },
        Sums = [new("debito", row => row.Debit), new("credito", row => row.Credit)],
        Derive = Balance("debito", "credito", "saldo"),
        DefaultSort = [(Value<LineRow, DateOnly>(row => row.Date), false), (Value<LineRow, string>(row => row.Reference), false), (Value<LineRow, int>(row => row.Sequence), false)],
        TieBreak = Value<LineRow, Guid>(row => row.Id),
        Link = row => new ListingRowLink(row.Reference, row.PartyId),
        HiddenUnlessFiltered = ("estado", row => row.Status != JournalEntryStatus.Voided),
    };

    // ---- Saldos por conta e mês ----

    internal sealed class BalanceRow
    {
        public string AccountCode { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public int Year { get; set; }

        public int Month { get; set; }

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }
    }

    private ListingBinding<BalanceRow> Balances() => new()
    {
        Base = companyId => context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.JournalEntry.Status != JournalEntryStatus.Voided)
            .GroupBy(line => new { line.Account.Code, line.Account.Name, line.JournalEntry.EntryDate.Year, line.JournalEntry.EntryDate.Month })
            .Select(group => new BalanceRow
            {
                AccountCode = group.Key.Code,
                AccountName = group.Key.Name,
                Year = group.Key.Year,
                Month = group.Key.Month,
                Debit = group.Sum(line => line.Debit),
                Credit = group.Sum(line => line.Credit),
            }),
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["conta"] = Value<BalanceRow, string>(row => row.AccountCode),
            ["nomeConta"] = Value<BalanceRow, string>(row => row.AccountName),
            ["classe"] = Value<BalanceRow, string>(row => row.AccountCode.Substring(0, 1)),
            ["ano"] = Value<BalanceRow, int>(row => row.Year),
            ["mes"] = Value<BalanceRow, int>(row => row.Month),
            ["debito"] = Value<BalanceRow, decimal>(row => row.Debit),
            ["credito"] = Value<BalanceRow, decimal>(row => row.Credit),
            ["saldo"] = Value<BalanceRow, decimal>(row => row.Debit - row.Credit),
        },
        GroupKeys = new Dictionary<string, Expression<Func<BalanceRow, string?>>>(StringComparer.Ordinal)
        {
            ["conta"] = row => row.AccountCode,
            ["nomeConta"] = row => row.AccountName,
            ["classe"] = row => row.AccountCode.Substring(0, 1),
            ["ano"] = row => row.Year.ToString(),
            ["mes"] = row => row.Month.ToString(),
        },
        Sums = [new("debito", row => row.Debit), new("credito", row => row.Credit)],
        Derive = Balance("debito", "credito", "saldo"),
        DefaultSort = [(Value<BalanceRow, string>(row => row.AccountCode), false), (Value<BalanceRow, int>(row => row.Year), false), (Value<BalanceRow, int>(row => row.Month), false)],
        TieBreak = Value<BalanceRow, string>(row => row.AccountName),
        Link = row => new ListingRowLink(null, null),
    };

    // ---- Documentos de venda ----

    internal sealed class SalesRow
    {
        public Guid Id { get; set; }

        public DateOnly Date { get; set; }

        public string InvoiceNo { get; set; } = string.Empty;

        public string InvoiceType { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string CustomerTaxId { get; set; } = string.Empty;

        public string? Country { get; set; }

        public bool IsVoided { get; set; }

        public string? EntryReference { get; set; }

        public decimal Net { get; set; }

        public decimal Tax { get; set; }

        public decimal Gross { get; set; }
    }

    private ListingBinding<SalesRow> Sales() => new()
    {
        Base = companyId => context.SalesDocuments
            .AsNoTracking()
            .Where(document => document.CompanyId == companyId)
            .Select(document => new SalesRow
            {
                Id = document.Id,
                Date = document.InvoiceDate,
                InvoiceNo = document.InvoiceNo,
                InvoiceType = document.InvoiceType,
                CustomerName = document.CustomerName,
                CustomerTaxId = document.CustomerTaxId,
                Country = document.CustomerCountry,
                IsVoided = document.IsVoided,
                EntryReference = document.JournalEntryReference,
                Net = document.NetTotal,
                Tax = document.TaxPayable,
                Gross = document.GrossTotal,
            }),
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["data"] = Value<SalesRow, DateOnly>(row => row.Date),
            ["documento"] = Value<SalesRow, string>(row => row.InvoiceNo),
            ["tipo"] = Value<SalesRow, string>(row => row.InvoiceType),
            ["cliente"] = Value<SalesRow, string>(row => row.CustomerName),
            ["nif"] = Value<SalesRow, string>(row => row.CustomerTaxId),
            ["pais"] = Value<SalesRow, string?>(row => row.Country),
            ["estado"] = Value<SalesRow, string>(row => row.IsVoided ? "Anulado" : "Válido"),
            ["contabilizado"] = Value<SalesRow, string>(row => row.EntryReference != null ? "Sim" : "Não"),
            ["lancamento"] = Value<SalesRow, string?>(row => row.EntryReference),
            ["ano"] = Value<SalesRow, int>(row => row.Date.Year),
            ["mes"] = Value<SalesRow, int>(row => row.Date.Month),
            ["liquido"] = Value<SalesRow, decimal>(row => row.Net),
            ["iva"] = Value<SalesRow, decimal>(row => row.Tax),
            ["total"] = Value<SalesRow, decimal>(row => row.Gross),
        },
        GroupKeys = new Dictionary<string, Expression<Func<SalesRow, string?>>>(StringComparer.Ordinal)
        {
            ["tipo"] = row => row.InvoiceType,
            ["cliente"] = row => row.CustomerName,
            ["pais"] = row => row.Country,
            ["estado"] = row => row.IsVoided ? "Anulado" : "Válido",
            ["contabilizado"] = row => row.EntryReference != null ? "Sim" : "Não",
            ["ano"] = row => row.Date.Year.ToString(),
            ["mes"] = row => row.Date.Month.ToString(),
        },
        Sums = [new("liquido", row => row.Net), new("iva", row => row.Tax), new("total", row => row.Gross)],
        DefaultSort = [(Value<SalesRow, DateOnly>(row => row.Date), false), (Value<SalesRow, string>(row => row.InvoiceNo), false)],
        TieBreak = Value<SalesRow, Guid>(row => row.Id),
        Link = row => new ListingRowLink(row.EntryReference, null),
        HiddenUnlessFiltered = ("estado", row => !row.IsVoided),
    };

    // ---- Compras do e-Fatura ----

    internal sealed class PurchaseRow
    {
        public Guid Id { get; set; }

        public DateOnly Date { get; set; }

        public string InvoiceNo { get; set; } = string.Empty;

        public string InvoiceType { get; set; } = string.Empty;

        public string SupplierName { get; set; } = string.Empty;

        public string SupplierTaxId { get; set; } = string.Empty;

        public string? Status { get; set; }

        public string? Sector { get; set; }

        public bool Dismissed { get; set; }

        public string? EntryReference { get; set; }

        public decimal TaxableBase { get; set; }

        public decimal Tax { get; set; }

        public decimal Gross { get; set; }
    }

    private ListingBinding<PurchaseRow> Purchases() => new()
    {
        Base = companyId => context.EFaturaPurchases
            .AsNoTracking()
            .Where(purchase => purchase.CompanyId == companyId)
            .Select(purchase => new PurchaseRow
            {
                Id = purchase.Id,
                Date = purchase.InvoiceDate,
                InvoiceNo = purchase.InvoiceNo,
                InvoiceType = purchase.InvoiceType,
                SupplierName = purchase.SupplierName,
                SupplierTaxId = purchase.SupplierTaxId,
                Status = purchase.Status,
                Sector = purchase.Sector,
                Dismissed = purchase.DismissedAtUtc != null,
                EntryReference = purchase.JournalEntryReference,
                TaxableBase = purchase.TaxBase,
                Tax = purchase.TaxAmount,
                Gross = purchase.GrossTotal,
            }),
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["data"] = Value<PurchaseRow, DateOnly>(row => row.Date),
            ["documento"] = Value<PurchaseRow, string>(row => row.InvoiceNo),
            ["tipo"] = Value<PurchaseRow, string>(row => row.InvoiceType),
            ["fornecedor"] = Value<PurchaseRow, string>(row => row.SupplierName),
            ["nif"] = Value<PurchaseRow, string>(row => row.SupplierTaxId),
            ["estadoAt"] = Value<PurchaseRow, string?>(row => row.Status),
            ["setor"] = Value<PurchaseRow, string?>(row => row.Sector),
            ["situacao"] = Value<PurchaseRow, string>(row => row.Dismissed ? "Dispensada" : row.EntryReference != null ? "Lançada" : "Por lançar"),
            ["lancamento"] = Value<PurchaseRow, string?>(row => row.EntryReference),
            ["ano"] = Value<PurchaseRow, int>(row => row.Date.Year),
            ["mes"] = Value<PurchaseRow, int>(row => row.Date.Month),
            ["base"] = Value<PurchaseRow, decimal>(row => row.TaxableBase),
            ["iva"] = Value<PurchaseRow, decimal>(row => row.Tax),
            ["total"] = Value<PurchaseRow, decimal>(row => row.Gross),
        },
        GroupKeys = new Dictionary<string, Expression<Func<PurchaseRow, string?>>>(StringComparer.Ordinal)
        {
            ["tipo"] = row => row.InvoiceType,
            ["fornecedor"] = row => row.SupplierName,
            ["estadoAt"] = row => row.Status,
            ["setor"] = row => row.Sector,
            ["situacao"] = row => row.Dismissed ? "Dispensada" : row.EntryReference != null ? "Lançada" : "Por lançar",
            ["ano"] = row => row.Date.Year.ToString(),
            ["mes"] = row => row.Date.Month.ToString(),
        },
        Sums = [new("base", row => row.TaxableBase), new("iva", row => row.Tax), new("total", row => row.Gross)],
        DefaultSort = [(Value<PurchaseRow, DateOnly>(row => row.Date), false), (Value<PurchaseRow, string>(row => row.InvoiceNo), false)],
        TieBreak = Value<PurchaseRow, Guid>(row => row.Id),
        Link = row => new ListingRowLink(row.EntryReference, null),
    };

    // ---- Ativos fixos ----

    internal sealed class AssetRow
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? AccountCode { get; set; }

        public DateOnly Acquisition { get; set; }

        public DateOnly StartOfUse { get; set; }

        public DateOnly? Disposal { get; set; }

        public bool IsActive { get; set; }

        public decimal AcquisitionValue { get; set; }

        public decimal ResidualValue { get; set; }
    }

    private ListingBinding<AssetRow> Assets() => new()
    {
        Base = companyId => context.FixedAssets
            .AsNoTracking()
            .Where(asset => asset.CompanyId == companyId)
            .Select(asset => new AssetRow
            {
                Id = asset.Id,
                Code = asset.Code,
                Description = asset.Description,
                AccountCode = asset.AssetAccount == null ? null : asset.AssetAccount.Code,
                Acquisition = asset.AcquisitionDate,
                StartOfUse = asset.StartOfUseDate,
                Disposal = asset.DisposalDate,
                IsActive = asset.IsActive,
                AcquisitionValue = asset.AcquisitionValue,
                ResidualValue = asset.ResidualValue,
            }),
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["codigo"] = Value<AssetRow, string>(row => row.Code),
            ["descricao"] = Value<AssetRow, string>(row => row.Description),
            ["conta"] = Value<AssetRow, string?>(row => row.AccountCode),
            ["aquisicao"] = Value<AssetRow, DateOnly>(row => row.Acquisition),
            ["inicioUso"] = Value<AssetRow, DateOnly>(row => row.StartOfUse),
            ["abate"] = Value<AssetRow, DateOnly?>(row => row.Disposal),
            ["estado"] = Value<AssetRow, string>(row => row.Disposal != null ? "Abatido" : row.IsActive ? "Em uso" : "Inativo"),
            ["ano"] = Value<AssetRow, int>(row => row.Acquisition.Year),
            ["valorAquisicao"] = Value<AssetRow, decimal>(row => row.AcquisitionValue),
            ["valorResidual"] = Value<AssetRow, decimal>(row => row.ResidualValue),
        },
        GroupKeys = new Dictionary<string, Expression<Func<AssetRow, string?>>>(StringComparer.Ordinal)
        {
            ["conta"] = row => row.AccountCode,
            ["estado"] = row => row.Disposal != null ? "Abatido" : row.IsActive ? "Em uso" : "Inativo",
            ["ano"] = row => row.Acquisition.Year.ToString(),
        },
        Sums = [new("valorAquisicao", row => row.AcquisitionValue), new("valorResidual", row => row.ResidualValue)],
        DefaultSort = [(Value<AssetRow, string>(row => row.Code), false)],
        TieBreak = Value<AssetRow, Guid>(row => row.Id),
        Link = row => new ListingRowLink(null, null),
    };

    // ---- Movimentos bancários ----

    internal sealed class BankRow
    {
        public Guid Id { get; set; }

        public DateOnly Date { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Reference { get; set; }

        public string AccountCode { get; set; } = string.Empty;

        public bool Reconciled { get; set; }

        public decimal Amount { get; set; }
    }

    private ListingBinding<BankRow> Bank() => new()
    {
        Base = companyId =>
            from line in context.BankStatementLines.AsNoTracking()
            where line.CompanyId == companyId
            join account in context.Accounts.AsNoTracking() on line.AccountId equals account.Id
            select new BankRow
            {
                Id = line.Id,
                Date = line.BookingDate,
                Description = line.Description,
                Reference = line.Reference,
                AccountCode = account.Code,
                Reconciled = line.ReconciliationId != null,
                Amount = line.Amount,
            },
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["data"] = Value<BankRow, DateOnly>(row => row.Date),
            ["descricao"] = Value<BankRow, string>(row => row.Description),
            ["referencia"] = Value<BankRow, string?>(row => row.Reference),
            ["conta"] = Value<BankRow, string>(row => row.AccountCode),
            ["conciliado"] = Value<BankRow, string>(row => row.Reconciled ? "Sim" : "Não"),
            ["ano"] = Value<BankRow, int>(row => row.Date.Year),
            ["mes"] = Value<BankRow, int>(row => row.Date.Month),
            ["valor"] = Value<BankRow, decimal>(row => row.Amount),
        },
        GroupKeys = new Dictionary<string, Expression<Func<BankRow, string?>>>(StringComparer.Ordinal)
        {
            ["conta"] = row => row.AccountCode,
            ["conciliado"] = row => row.Reconciled ? "Sim" : "Não",
            ["ano"] = row => row.Date.Year.ToString(),
            ["mes"] = row => row.Date.Month.ToString(),
        },
        Sums = [new("valor", row => row.Amount)],
        DefaultSort = [(Value<BankRow, DateOnly>(row => row.Date), false)],
        TieBreak = Value<BankRow, Guid>(row => row.Id),
        Link = row => new ListingRowLink(null, null),
    };

    // ---- Arquivo digital ----

    internal sealed class ArchiveRow
    {
        public Guid Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public string? IssuerTaxId { get; set; }

        public string? DocumentNo { get; set; }

        public DateOnly? DocumentDate { get; set; }

        public string? EntryReference { get; set; }

        public string? Notes { get; set; }

        public decimal Gross { get; set; }

        public decimal Tax { get; set; }
    }

    private ListingBinding<ArchiveRow> Archive() => new()
    {
        Base = companyId => context.DocumentAttachments
            .AsNoTracking()
            .Where(document => document.CompanyId == companyId)
            .Select(document => new ArchiveRow
            {
                Id = document.Id,
                FileName = document.FileName,
                ContentType = document.ContentType,
                IssuerTaxId = document.QrIssuerTaxId,
                DocumentNo = document.QrDocumentNo,
                DocumentDate = document.QrDocumentDate,
                EntryReference = document.JournalEntryReference,
                Notes = document.Notes,
                Gross = document.QrGrossTotal ?? 0m,
                Tax = document.QrTaxTotal ?? 0m,
            }),
        Values = new Dictionary<string, LambdaExpression>(StringComparer.Ordinal)
        {
            ["ficheiro"] = Value<ArchiveRow, string>(row => row.FileName),
            ["tipo"] = Value<ArchiveRow, string>(row => row.ContentType),
            ["nifEmissor"] = Value<ArchiveRow, string?>(row => row.IssuerTaxId),
            ["documento"] = Value<ArchiveRow, string?>(row => row.DocumentNo),
            ["dataDocumento"] = Value<ArchiveRow, DateOnly?>(row => row.DocumentDate),
            ["ligado"] = Value<ArchiveRow, string>(row => row.EntryReference != null ? "Sim" : "Não"),
            ["lancamento"] = Value<ArchiveRow, string?>(row => row.EntryReference),
            ["notas"] = Value<ArchiveRow, string?>(row => row.Notes),
            ["total"] = Value<ArchiveRow, decimal>(row => row.Gross),
            ["iva"] = Value<ArchiveRow, decimal>(row => row.Tax),
        },
        GroupKeys = new Dictionary<string, Expression<Func<ArchiveRow, string?>>>(StringComparer.Ordinal)
        {
            ["tipo"] = row => row.ContentType,
            ["nifEmissor"] = row => row.IssuerTaxId,
            ["ligado"] = row => row.EntryReference != null ? "Sim" : "Não",
        },
        Sums = [new("total", row => row.Gross), new("iva", row => row.Tax)],
        DefaultSort = [(Value<ArchiveRow, string>(row => row.FileName), false)],
        TieBreak = Value<ArchiveRow, Guid>(row => row.Id),
        Link = row => new ListingRowLink(row.EntryReference, null),
    };
}
