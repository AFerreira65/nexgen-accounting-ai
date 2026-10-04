using System.Text.Json;
using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha de uma demonstração financeira, com o valor do período e o comparativo.</summary>
/// <param name="Section">A secção (ex.: «Ativo não corrente») ou vazio numa linha de total geral.</param>
/// <param name="IsTotal">Se é um subtotal ou total, calculado a partir das linhas anteriores.</param>
public sealed record StatementLine(string Section, string Label, decimal Amount, decimal Comparative, bool IsTotal = false);

/// <summary>Uma conta com saldo que não entrou na demonstração, e porquê.</summary>
public sealed record StatementExcludedAccount(string AccountCode, string AccountName, decimal Balance, string Reason);

public sealed record BalanceSheetStatement(
    DateOnly Date,
    DateOnly ComparativeDate,
    string Model,
    IReadOnlyList<StatementLine> Lines,
    decimal TotalAssets,
    decimal TotalEquity,
    decimal TotalLiabilities,
    decimal Difference,
    IReadOnlyList<StatementExcludedAccount> Excluded,
    string Source);

public sealed record IncomeStatementByNature(
    DateOnly From,
    DateOnly To,
    string Model,
    IReadOnlyList<StatementLine> Lines,
    decimal NetResult,
    IReadOnlyList<StatementExcludedAccount> Excluded,
    string Source);

/// <summary>
/// A correspondência oficial entre cada taxonomia e as rubricas do balanço e da
/// demonstração de resultados, transcrita do ficheiro «Saldos e Demonstrações
/// Financeiras por Taxonomia» da AT (recurso <c>taxonomy-statement-lines.json</c>).
/// </summary>
public static class StatementLineCatalog
{
    /// <summary>Uma taxonomia: a classe, o saldo esperado e a rubrica para saldo devedor e para saldo credor.</summary>
    public sealed record Entry(string? Class, string? Balance, string? DebitLine, string? CreditLine);

    private sealed record RawEntry(int Code, string? Class, string? Balance, string?[]? Base, string?[]? Pe, string?[]? Me, string?[]? Micro);

    private sealed record RawFile(string Source, List<RawEntry> S, List<RawEntry> M);

    private static readonly Lazy<RawFile> File = new(Load);

    public static string Source => File.Value.Source;

    /// <summary>
    /// A entrada de uma taxonomia para o referencial da conta (<c>S</c> ou
    /// <c>M</c>) e o modelo de demonstrações (<c>base</c>, <c>pe</c> ou
    /// <c>me</c>; o referencial <c>M</c> só tem um modelo).
    /// </summary>
    public static Entry? Find(string taxonomyReference, int taxonomyCode, string model)
    {
        var micro = string.Equals(taxonomyReference, "M", StringComparison.OrdinalIgnoreCase);
        var raw = (micro ? ByCodeMicro : ByCodeBase).Value.GetValueOrDefault(taxonomyCode);
        if (raw is null)
        {
            return null;
        }

        var lines = micro ? raw.Micro : model switch
        {
            "pe" => raw.Pe,
            "me" => raw.Me,
            _ => raw.Base,
        };
        return new Entry(raw.Class, raw.Balance, lines?[0], lines?[1]);
    }

    private static readonly Lazy<Dictionary<int, RawEntry>> ByCodeBase = new(() => File.Value.S.ToDictionary(item => item.Code));

    private static readonly Lazy<Dictionary<int, RawEntry>> ByCodeMicro = new(() => File.Value.M.ToDictionary(item => item.Code));

    private static RawFile Load()
    {
        using var stream = typeof(StatementLineCatalog).Assembly
            .GetManifestResourceStream("AccountingAI.Infrastructure.Persistence.Resources.taxonomy-statement-lines.json")
            ?? throw new InvalidOperationException("O recurso taxonomy-statement-lines.json não está embebido.");
        return JsonSerializer.Deserialize<RawFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("O recurso taxonomy-statement-lines.json está vazio.");
    }
}

/// <summary>
/// Monta o balanço e a demonstração de resultados por naturezas nos modelos do
/// SNC a partir das taxonomias das contas. Só lê; exclui os lançamentos
/// anulados. O balanço usa os movimentos do exercício até à data (o lançamento
/// de abertura traz os saldos anteriores); a demonstração de resultados usa o
/// período sem os lançamentos de apuramento. As contas com saldo que não cabem
/// em nenhuma rubrica — sem taxonomia, ou com uma taxonomia que a AT não
/// representa nas demonstrações — são listadas à parte, em vez de escondidas.
/// </summary>
public sealed partial class FinancialStatementsService(AccountingDbContext context)
{
    private const string ResultLine = "Resultado líquido do período";

    private static readonly (string Key, string Title, string[] Order)[] BalanceSections =
    [
        ("ANC", "Ativo não corrente",
        [
            "Ativos fixos tangíveis", "Propriedades de investimento", "Goodwill", "Ativos intangíveis",
            "Ativos biológicos (não corrente)", "Participações financeiras - método da equivalência patrimonial",
            "Outros investimentos financeiros", "Investimentos financeiros", "Créditos a receber",
            "Créditos e outros ativos não correntes", "Ativos por Impostos diferidos",
        ]),
        ("AC", "Ativo corrente",
        [
            "Inventários", "Ativos biológicos (corrente)", "Clientes", "Estado e outros entes públicos (balanço ativo)",
            "Capital subscrito e não realizado", "Outros créditos a receber", "Diferimentos (balanço ativo)",
            "Ativos financeiros detidos para negociação", "Outros ativos financeiros", "Outros ativos correntes",
            "Ativos não correntes detidos para venda", "Caixa e depósitos bancários",
        ]),
        ("C", "Capital próprio",
        [
            "Capital Subscrito", "Ações (quotas) próprias", "Outros instrumentos de capital próprio", "Prémios de emissão",
            "Reservas legais", "Outras reservas", "Resultados transitados", "Excedentes de revalorização",
            "Ajustamentos / Outras variações no capital próprio", ResultLine, "Dividendos antecipados",
        ]),
        ("PNC", "Passivo não corrente",
        [
            "Provisões", "Financiamentos obtidos (não corrente)", "Responsabilidades por benefícios pós-emprego",
            "Passivos por impostos diferidos", "Outras dívidas a pagar (não corrente)",
        ]),
        ("PC", "Passivo corrente",
        [
            "Fornecedores", "Adiantamentos de clientes", "Estado e outros entes públicos (balanço passivo)",
            "Financiamentos obtidos (corrente)", "Outras dívidas a pagar (corrente)", "Diferimentos (balanço passivo)",
            "Passivos financeiros detidos para negociação", "Outros passivos financeiros", "Outros passivos correntes",
            "Passivos não correntes detidos para venda",
        ]),
    ];

    // A demonstração de resultados por naturezas: as rubricas e os três subtotais, pela ordem do modelo.
    private static readonly string[] IncomeBeforeDepreciation =
    [
        "Vendas e serviços prestados", "Subsídios à exploração",
        "Ganhos / perdas imputados de subsidiárias, associadas e empreendimentos conjuntos",
        "Variação nos inventários da produção", "Trabalhos para a própria entidade",
        "Custo das mercadorias vendidas e das matérias consumidas", "Fornecimentos e serviços externos", "Gastos com o pessoal",
        "Imparidade / Ajustamentos de inventários (perdas/reversões)", "Imparidade de dívidas a receber (perdas/reversões)",
        "Provisões (aumentos/reduções)", "Imparidade de investimentos não depreciáveis/amortizáveis (perdas/reversões)",
        "Outras Imparidades (Perdas/Reversões)", "Imparidades (Perdas/Reversões)", "Aumentos / reduções de justo valor",
        "Outros rendimentos", "Outros gastos",
    ];

    private static readonly string[] IncomeBeforeOperating =
    [
        "Gastos / reversões de depreciação e de amortização",
        "Imparidade de investimentos depreciáveis / amortizáveis (perdas/reversões)",
    ];

    private static readonly string[] IncomeBeforeTax = ["Juros e rendimentos similares obtidos", "Juros e gastos similares suportados"];

    private const string TaxLine = "Imposto sobre o rendimento do período";

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<BalanceSheetStatement?> BalanceSheetAsync(
        Guid userId, Guid companyId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var model = ModelOf(company);
        var comparativeDate = date.AddYears(-1);
        var excluded = new List<StatementExcludedAccount>();
        var current = BalanceLines(
            await BalancesAsync(companyId, new DateOnly(date.Year, 1, 1), date, includeClosing: true, cancellationToken).ConfigureAwait(false),
            model, excluded);
        var comparative = BalanceLines(
            await BalancesAsync(companyId, new DateOnly(comparativeDate.Year, 1, 1), comparativeDate, includeClosing: true, cancellationToken).ConfigureAwait(false),
            model, null);

        var lines = new List<StatementLine>();
        var totals = new Dictionary<string, (decimal Current, decimal Comparative)>();
        foreach (var (key, title, order) in BalanceSections)
        {
            var labels = order
                .Concat(current.Keys.Concat(comparative.Keys).Where(item => item.Section == key).Select(item => item.Line))
                .Distinct(StringComparer.Ordinal);
            decimal sum = 0m, comparativeSum = 0m;
            foreach (var label in labels)
            {
                var amount = current.GetValueOrDefault((key, label));
                var previous = comparative.GetValueOrDefault((key, label));
                if (amount == 0m && previous == 0m)
                {
                    continue;
                }

                lines.Add(new StatementLine(title, DisplayLabel(label), amount, previous));
                sum += amount;
                comparativeSum += previous;
            }

            lines.Add(new StatementLine(title, $"Total do {title.ToLowerInvariant()}", sum, comparativeSum, IsTotal: true));
            totals[key] = (sum, comparativeSum);
        }

        var assets = totals["ANC"].Current + totals["AC"].Current;
        var equity = totals["C"].Current;
        var liabilities = totals["PNC"].Current + totals["PC"].Current;
        lines.Add(new StatementLine(string.Empty, "Total do ativo", assets, totals["ANC"].Comparative + totals["AC"].Comparative, IsTotal: true));
        lines.Add(new StatementLine(string.Empty, "Total do passivo", liabilities, totals["PNC"].Comparative + totals["PC"].Comparative, IsTotal: true));
        lines.Add(new StatementLine(
            string.Empty, "Total do capital próprio e do passivo", equity + liabilities,
            totals["C"].Comparative + totals["PNC"].Comparative + totals["PC"].Comparative, IsTotal: true));

        return new BalanceSheetStatement(
            date, comparativeDate, ModelName(model), lines, assets, equity, liabilities, assets - (equity + liabilities),
            excluded.OrderBy(item => item.AccountCode, StringComparer.Ordinal).ToList(), StatementLineCatalog.Source);
    }

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<IncomeStatementByNature?> IncomeStatementAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var company = await FindCompanyAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var model = ModelOf(company);
        var excluded = new List<StatementExcludedAccount>();
        var current = IncomeLines(
            await BalancesAsync(companyId, from, to, includeClosing: false, cancellationToken).ConfigureAwait(false), model, excluded);
        var comparative = IncomeLines(
            await BalancesAsync(companyId, from.AddYears(-1), to.AddYears(-1), includeClosing: false, cancellationToken).ConfigureAwait(false),
            model, null);

        var lines = new List<StatementLine>();
        decimal running = 0m, comparativeRunning = 0m;
        var used = new HashSet<string>(StringComparer.Ordinal);

        void Add(IEnumerable<string> labels)
        {
            foreach (var label in labels)
            {
                if (!used.Add(label))
                {
                    continue;
                }

                var amount = current.GetValueOrDefault(label);
                var previous = comparative.GetValueOrDefault(label);
                if (amount == 0m && previous == 0m)
                {
                    continue;
                }

                lines.Add(new StatementLine(string.Empty, label, amount, previous));
                running += amount;
                comparativeRunning += previous;
            }
        }

        void Total(string label) => lines.Add(new StatementLine(string.Empty, label, running, comparativeRunning, IsTotal: true));

        // As rubricas que o modelo não prevê nesta ordem entram antes do primeiro subtotal.
        var known = IncomeBeforeDepreciation.Concat(IncomeBeforeOperating).Concat(IncomeBeforeTax).Append(TaxLine).ToHashSet(StringComparer.Ordinal);
        Add(IncomeBeforeDepreciation);
        Add(current.Keys.Concat(comparative.Keys).Where(label => !known.Contains(label)).Order(StringComparer.Ordinal));
        Total("Resultado antes de depreciações, gastos de financiamento e impostos");
        Add(IncomeBeforeOperating);
        Total("Resultado operacional (antes de gastos de financiamento e impostos)");
        Add(IncomeBeforeTax);
        Total("Resultado antes de impostos");
        Add([TaxLine]);
        Total(ResultLine);

        return new IncomeStatementByNature(
            from, to, ModelName(model), lines, running,
            excluded.OrderBy(item => item.AccountCode, StringComparer.Ordinal).ToList(), StatementLineCatalog.Source);
    }

    private static Dictionary<(string Section, string Line), decimal> BalanceLines(
        IReadOnlyList<AccountBalance> balances, string model, List<StatementExcludedAccount>? excluded)
    {
        var lines = new Dictionary<(string Section, string Line), decimal>();
        void Sum(string section, string line, decimal amount) => lines[(section, line)] = lines.GetValueOrDefault((section, line)) + amount;

        foreach (var account in balances)
        {
            var net = account.Debit - account.Credit;
            if (net == 0m)
            {
                continue;
            }

            var entry = account.TaxonomyCode is { } code && account.TaxonomyReference is { } reference
                ? StatementLineCatalog.Find(reference, code, model)
                : null;
            if (entry is null)
            {
                excluded?.Add(new(account.Code, account.Name, net, account.TaxonomyCode is null ? "Sem código de taxonomia" : "Taxonomia desconhecida"));
                continue;
            }

            // O resultado do período no balanço é o saldo das contas de gastos, de rendimentos
            // e de resultados: antes ou depois do apuramento dá o mesmo.
            if (entry.Class is "GR" or "RG" ||
                (entry.CreditLine ?? entry.DebitLine)?.StartsWith(ResultLine, StringComparison.Ordinal) == true)
            {
                Sum("C", ResultLine, -net);
                continue;
            }

            if (entry.Class is not ("AC" or "ANC" or "APC" or "APNC" or "PC" or "PNC" or "C"))
            {
                excluded?.Add(new(account.Code, account.Name, net, "A taxonomia não é representada no balanço (ex.: compras antes da transferência para inventários)"));
                continue;
            }

            // Saldo devedor vai para a rubrica do campo débito (ativo); credor, para a do campo
            // crédito (passivo ou capital). Quando só há uma rubrica, a conta entra nela com o seu sinal.
            var useDebit = entry.DebitLine is not null && (net > 0m || entry.CreditLine is null);
            if (useDebit)
            {
                Sum(entry.Class is "APNC" or "ANC" or "PNC" ? "ANC" : "AC", entry.DebitLine!, net);
            }
            else if (entry.CreditLine is not null)
            {
                var section = entry.Class switch
                {
                    "C" => "C",
                    "APNC" or "PNC" or "ANC" => "PNC",
                    _ => "PC",
                };
                Sum(section, entry.CreditLine, -net);
            }
            else
            {
                excluded?.Add(new(account.Code, account.Name, net, "A taxonomia não indica rubrica"));
            }
        }

        return lines;
    }

    private static Dictionary<string, decimal> IncomeLines(
        IReadOnlyList<AccountBalance> balances, string model, List<StatementExcludedAccount>? excluded)
    {
        var lines = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var account in balances)
        {
            // Rendimentos menos gastos: positivo aumenta o resultado.
            var amount = account.Credit - account.Debit;
            if (amount == 0m)
            {
                continue;
            }

            var entry = account.TaxonomyCode is { } code && account.TaxonomyReference is { } reference
                ? StatementLineCatalog.Find(reference, code, model)
                : null;
            if (entry is null)
            {
                // Sem taxonomia não se sabe a natureza: só as classes 6, 7 e 8 interessam aqui.
                if (account.Code[0] is '6' or '7' or '8')
                {
                    excluded?.Add(new(account.Code, account.Name, amount, account.TaxonomyCode is null ? "Sem código de taxonomia" : "Taxonomia desconhecida"));
                }

                continue;
            }

            if (IncomeLineLabel(account.TaxonomyReference, account.TaxonomyCode, model) is { } line)
            {
                lines[line] = lines.GetValueOrDefault(line) + amount;
            }
        }

        return lines;
    }

    /// <summary>A rubrica da demonstração de resultados de uma taxonomia; <c>null</c> quando não é de gastos nem de rendimentos.</summary>
    private static string? IncomeLineLabel(string? taxonomyReference, int? taxonomyCode, string model)
    {
        var entry = taxonomyCode is { } code && taxonomyReference is { } reference
            ? StatementLineCatalog.Find(reference, code, model)
            : null;
        if (entry is null || entry.Class is not ("GR" or "RG"))
        {
            // Resultado líquido (818) e dividendos antecipados (89) são do balanço.
            return null;
        }

        // O resultado antes de impostos (811) é um subtotal calculado, não uma rubrica.
        var line = entry.DebitLine ?? entry.CreditLine;
        return line == "Resultado antes de impostos" ? null : line;
    }

    private async Task<IReadOnlyList<AccountBalance>> BalancesAsync(
        Guid companyId, DateOnly from, DateOnly to, bool includeClosing, CancellationToken cancellationToken)
    {
        var lines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to);
        if (!includeClosing)
        {
            // O apuramento gerado pela aplicação está marcado; o que veio de um SAF-T
            // reconhece-se por movimentar as contas de resultados (811 e 818).
            lines = lines.Where(line =>
                line.JournalEntry.Source != EntrySource.Closing &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("811") || other.Account.Code.StartsWith("818")));
        }

        return await lines
            .GroupBy(line => new { line.Account.Code, line.Account.Name, line.Account.TaxonomyReference, line.Account.TaxonomyCode })
            .Select(group => new AccountBalance(
                group.Key.Code, group.Key.Name, group.Key.TaxonomyReference, group.Key.TaxonomyCode,
                group.Sum(line => line.Debit), group.Sum(line => line.Credit)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ModelOf(Company company) => company.AccountingStandard switch
    {
        AccountingStandard.SncSmallEntities => "pe",
        AccountingStandard.SncMicroEntities => "me",
        _ => "base",
    };

    private static string ModelName(string model) => model switch
    {
        "pe" => "SNC — modelo reduzido (pequenas entidades)",
        "me" => "SNC — microentidades",
        _ => "SNC — modelo geral",
    };

    /// <summary>A secção já diz se é ativo ou passivo, corrente ou não: o sufixo entre parênteses sai do rótulo.</summary>
    private static string DisplayLabel(string line)
    {
        foreach (var suffix in new[] { " (balanço ativo)", " (balanço passivo)", " (corrente)", " (não corrente)" })
        {
            if (line.EndsWith(suffix, StringComparison.Ordinal))
            {
                return line[..^suffix.Length];
            }
        }

        return line;
    }

    private Task<Company?> FindCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);

    private sealed record AccountBalance(
        string Code, string Name, string? TaxonomyReference, int? TaxonomyCode, decimal Debit, decimal Credit);
}
