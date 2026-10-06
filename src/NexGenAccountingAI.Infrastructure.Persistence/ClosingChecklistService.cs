using NexGenAccountingAI.Application.Fiscal;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha do papel de trabalho de fecho.</summary>
/// <param name="Kind"><c>auto</c> (calculada) ou <c>manual</c> (o contabilista marca).</param>
/// <param name="State">Auto: <c>ok</c>, <c>attention</c> ou <c>info</c>. Manual: <c>done</c> ou <c>pending</c>.</param>
/// <param name="Link">O ecrã onde se trata do assunto.</param>
public sealed record ClosingItem(
    string Key,
    string Group,
    string Label,
    string Kind,
    string State,
    int? Count,
    string Detail,
    string? Link,
    string? Note,
    string? CheckedByName,
    DateTimeOffset? CheckedAtUtc);

public sealed record ClosingChecklist(
    int Year,
    int Month,
    string PeriodKey,
    IReadOnlyList<ClosingItem> Items,
    int Attention,
    int PendingManual,
    bool Ready);

/// <summary>
/// O papel de trabalho de fecho de um mês (F4F-02): o que ainda falta tratar antes de fechar. As
/// verificações automáticas calculam-se de cada vez a partir da contabilidade (lançamentos por
/// conferir, descabimentos, anomalias, compras e vendas por lançar, movimentos bancários por conciliar,
/// documentos por ligar, o IVA do período e as contas fora das demonstrações); as manuais marca-as o
/// contabilista, com uma nota, e ficam registadas com quem e quando. Não bloqueia nada: diz só se o
/// mês está pronto para fechar.
/// </summary>
public sealed class ClosingChecklistService(
    AccountingDbContext context,
    AnomalyService anomalies,
    ObligationCalendarService obligations,
    FinancialStatementsService statements,
    TimeProvider time)
{
    /// <summary>As verificações manuais, pela ordem em que se fazem.</summary>
    public static IReadOnlyList<(string Key, string Label, string Hint)> ManualItems { get; } =
    [
        ("bancos-conferidos", "Conferi os saldos bancários com os extratos", "O saldo de cada banco no fim do mês bate com o extrato."),
        ("acrescimos-diferimentos", "Revi os acréscimos e os diferimentos", "Gastos e rendimentos do mês ainda sem fatura, e o que pertence a meses seguintes."),
        ("provisoes-imparidades", "Revi as provisões e as imparidades", "Dívidas de cobrança duvidosa, inventários e litígios."),
        ("depreciacoes", "Confirmei as depreciações do mês", "O mapa de depreciações está lançado."),
        ("ordenados-encargos", "Confirmei os ordenados e os encargos sociais", "Processamento, retenções e Segurança Social do mês."),
        ("cliente-informado", "Enviei o resumo do mês ao cliente", "O relatório do mês seguiu para o responsável da empresa."),
    ];

    public async Task<ClosingChecklist?> BuildAsync(Guid userId, Guid companyId, int year, int month, CancellationToken cancellationToken = default)
    {
        ValidatePeriod(year, month);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var key = PeriodKey(year, month);
        var items = new List<ClosingItem>();

        ClosingItem Auto(string itemKey, string group, string label, int count, string detail, string link, string? okDetail = null) => new(
            itemKey, group, label, "auto", count == 0 ? "ok" : "attention", count, count == 0 ? okDetail ?? "Nada por tratar." : detail, link, null, null, null);

        IQueryable<JournalEntry> entries() => context.JournalEntries.AsNoTracking()
            .Where(entry => entry.CompanyId == companyId && entry.EntryDate >= from && entry.EntryDate <= to &&
                entry.Status != JournalEntryStatus.Voided && entry.Source != EntrySource.Opening && entry.Source != EntrySource.Closing);

        var pending = await entries().CountAsync(entry => entry.Status == JournalEntryStatus.PendingReview, cancellationToken).ConfigureAwait(false);
        items.Add(Auto("lancamentos-por-conferir", "Lançamentos", "Lançamentos por conferir", pending, $"{pending} lançamento(s) do mês ainda por rever.", "/movimentos"));

        var unbalanced = await context.JournalEntryLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.JournalEntry.EntryDate >= from && line.JournalEntry.EntryDate <= to && line.JournalEntry.Status != JournalEntryStatus.Voided)
            .GroupBy(line => line.JournalEntryId)
            .Where(group => group.Sum(line => line.Debit) != group.Sum(line => line.Credit))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        items.Add(Auto("lancamentos-descabidos", "Lançamentos", "Lançamentos com o débito diferente do crédito", unbalanced, $"{unbalanced} lançamento(s) não fecham.", "/movimentos"));

        var report = await anomalies.ScanAsync(userId, companyId, from, to, includeDismissed: false, cancellationToken).ConfigureAwait(false);
        var found = report?.Rules.Sum(rule => rule.Count) ?? 0;
        items.Add(Auto("anomalias", "Lançamentos", "Anomalias por tratar", found, $"{found} achado(s) por conferir ou dispensar.", "/anomalias"));

        var purchases = await context.EFaturaPurchases.AsNoTracking()
            .CountAsync(purchase => purchase.CompanyId == companyId && purchase.InvoiceDate >= from && purchase.InvoiceDate <= to &&
                purchase.JournalEntryReference == null && purchase.DismissedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        items.Add(Auto("compras-por-lancar", "Documentos", "Compras do e-Fatura por lançar", purchases, $"{purchases} fatura(s) de compra do mês sem lançamento.", "/lancamentos-sugeridos"));

        var sales = await context.SalesDocuments.AsNoTracking()
            .CountAsync(document => document.CompanyId == companyId && document.InvoiceDate >= from && document.InvoiceDate <= to &&
                !document.IsVoided && document.JournalEntryReference == null, cancellationToken)
            .ConfigureAwait(false);
        items.Add(Auto("vendas-por-contabilizar", "Documentos", "Vendas por contabilizar", sales, $"{sales} documento(s) de venda do mês sem lançamento.", "/lancamentos-sugeridos"));

        var archive = await context.DocumentAttachments.AsNoTracking()
            .CountAsync(document => document.CompanyId == companyId && document.JournalEntryReference == null &&
                document.UploadedAtUtc >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) &&
                document.UploadedAtUtc < new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), cancellationToken)
            .ConfigureAwait(false);
        items.Add(Auto("arquivo-por-ligar", "Documentos", "Documentos arquivados por ligar a um lançamento", archive, $"{archive} documento(s) carregados no mês sem lançamento.", "/arquivo"));

        var bank = await context.BankStatementLines.AsNoTracking()
            .CountAsync(line => line.CompanyId == companyId && line.BookingDate >= from && line.BookingDate <= to && line.ReconciliationId == null, cancellationToken)
            .ConfigureAwait(false);
        items.Add(Auto("bancos-por-conciliar", "Bancos", "Movimentos bancários por conciliar", bank, $"{bank} movimento(s) do mês por conciliar.", "/bancos"));

        items.Add(await VatItemAsync(userId, companyId, year, month, to, cancellationToken).ConfigureAwait(false));

        var balance = await statements.BalanceSheetAsync(userId, companyId, to, cancellationToken).ConfigureAwait(false);
        var outside = balance?.Excluded.Count ?? 0;
        items.Add(Auto("contas-fora-das-demonstracoes", "Demonstrações", "Contas com saldo fora das demonstrações", outside, $"{outside} conta(s) com saldo sem taxonomia: o balanço pode não fechar.", "/taxonomias"));

        // As manuais.
        var saved = await context.ClosingChecks.AsNoTracking().Where(item => item.CompanyId == companyId && item.PeriodKey == key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = await context.UserAccounts.AsNoTracking()
            .Where(user => saved.Select(item => item.CheckedBy).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);
        foreach (var (itemKey, label, hint) in ManualItems)
        {
            var row = saved.FirstOrDefault(item => item.ItemKey == itemKey);
            items.Add(new ClosingItem(
                itemKey, "Conferências", label, "manual", row is { Checked: true } ? "done" : "pending", null, hint, null, row?.Note,
                row?.CheckedBy is { } by ? names.GetValueOrDefault(by) : null, row?.CheckedAtUtc));
        }

        var attention = items.Count(item => item is { Kind: "auto", State: "attention" });
        var pendingManual = items.Count(item => item is { Kind: "manual", State: "pending" });
        return new ClosingChecklist(year, month, key, items, attention, pendingManual, attention == 0 && pendingManual == 0);
    }

    /// <summary>Marca ou desmarca uma verificação manual. <c>false</c> sem acesso à empresa; erro se a chave não existe.</summary>
    /// <exception cref="ArgumentException">A verificação não existe ou o período é inválido.</exception>
    public async Task<bool> SetManualAsync(
        Guid userId, Guid companyId, int year, int month, string itemKey, bool isChecked, string? note, CancellationToken cancellationToken = default)
    {
        ValidatePeriod(year, month);
        if (ManualItems.All(item => item.Key != itemKey))
        {
            throw new ArgumentException("Essa verificação não existe.", nameof(itemKey));
        }

        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var key = PeriodKey(year, month);
        var row = await context.ClosingChecks.FirstOrDefaultAsync(item => item.CompanyId == companyId && item.PeriodKey == key && item.ItemKey == itemKey, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = ClosingCheck.Create(companyId, key, itemKey);
            context.ClosingChecks.Add(row);
        }

        row.Set(isChecked, note, userId, time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<ClosingItem> VatItemAsync(Guid userId, Guid companyId, int year, int month, DateOnly monthEnd, CancellationToken cancellationToken)
    {
        // A declaração do mês (ou do trimestre que acaba nele) tem o prazo nos meses seguintes.
        var calendar = await obligations.CalendarAsync(userId, companyId, monthEnd, monthEnd.AddDays(100), cancellationToken).ConfigureAwait(false);
        var monthKey = $"{year}-{month:00}";
        var quarterKey = month % 3 == 0 ? $"{year}-T{month / 3}" : null;
        var row = calendar?.Rows.FirstOrDefault(item =>
            item.Code is ObligationCatalog.VatReturnMonthly or ObligationCatalog.VatReturnQuarterly && (item.PeriodKey == monthKey || item.PeriodKey == quarterKey));
        if (row is null)
        {
            return new ClosingItem("iva-do-periodo", "Fiscal", "Declaração periódica de IVA do período", "auto", "info", null, "Não há declaração de IVA a entregar a acabar neste mês.", "/obrigacoes", null, null, null);
        }

        var delivered = row.State != nameof(TaxObligationState.Pendente);
        return new ClosingItem(
            "iva-do-periodo", "Fiscal", "Declaração periódica de IVA do período", "auto", delivered ? "ok" : "attention", delivered ? 0 : 1,
            delivered ? $"{row.Name}: {row.State.ToLowerInvariant()}." : $"{row.Name} ({row.PeriodLabel}) por entregar até {row.DueDate:dd/MM/yyyy}.",
            "/declaracao-iva", null, null, null);
    }

    private static string PeriodKey(int year, int month) => $"{year}-{month:00}";

    private static void ValidatePeriod(int year, int month)
    {
        if (year is < 2000 or > 9999 || month is < 1 or > 12)
        {
            throw new ArgumentException("Indique um ano e um mês válidos.");
        }
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(company => company.Id == companyId && company.IsActive &&
                company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive), cancellationToken);
}
