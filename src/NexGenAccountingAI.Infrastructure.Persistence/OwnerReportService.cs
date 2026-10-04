using System.Globalization;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Um número do relatório, com o valor homólogo quando faz sentido.</summary>
public sealed record OwnerReportFigure(string Label, decimal Value, decimal? Comparative, string? Note);

/// <summary>Um alerta do relatório: factual, com o número que o justifica.</summary>
public sealed record OwnerReportAlert(string Severity, string Text);

/// <summary>
/// O relatório mensal para o empresário: os números do mês e do ano até ao mês,
/// com o homólogo; a tesouraria, os clientes e os fornecedores no fim do mês; a
/// idade dos saldos; o orçamento; frases escritas a partir dos números e alertas.
/// </summary>
public sealed record OwnerReport(
    int Year,
    int Month,
    string MonthLabel,
    IReadOnlyList<OwnerReportFigure> MonthFigures,
    IReadOnlyList<OwnerReportFigure> YearFigures,
    IReadOnlyList<OwnerReportFigure> Position,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<OwnerReportAlert> Alerts,
    MonthlyEvolution Evolution,
    PartyAging Receivables,
    PartyAging Payables,
    BudgetReport? Budget);

/// <summary>
/// Relatório para o empresário (F3-26A). Junta, sem IA, o que os mapas de gestão
/// já calculam — a evolução mensal, a idade dos saldos e o orçamento — e
/// escreve-o em frases simples, com alertas factuais: o resultado do mês
/// negativo, as vendas abaixo do homólogo, quantos meses de gastos a
/// tesouraria cobre, os saldos de clientes com mais de 90 dias e os desvios
/// desfavoráveis do orçamento. Nenhum limiar é uma referência setorial: são
/// critérios da aplicação, indicados no texto.
/// </summary>
public sealed class OwnerReportService(FinancialStatementsService statements, PartyAgingService aging, BudgetService budget)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-PT");

    public async Task<OwnerReport?> BuildAsync(Guid userId, Guid companyId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 9999 || month is < 1 or > 12)
        {
            throw new ArgumentException("Indique um exercício e um mês válidos.");
        }

        var evolution = await statements.MonthlyEvolutionAsync(userId, companyId, year, cancellationToken).ConfigureAwait(false);
        if (evolution is null)
        {
            return null;
        }

        var endOfMonth = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var receivables = (await aging.BuildAsync(userId, companyId, endOfMonth, PartyAgingService.Customers, cancellationToken).ConfigureAwait(false))!;
        var payables = (await aging.BuildAsync(userId, companyId, endOfMonth, PartyAgingService.Suppliers, cancellationToken).ConfigureAwait(false))!;
        var budgetReport = await budget.ReportAsync(userId, companyId, year, month, cancellationToken).ConfigureAwait(false);
        if (budgetReport is { Lines.Count: 0 })
        {
            budgetReport = null;
        }

        var current = evolution.Months.Single(item => item.Month == month);
        var toDate = evolution.Months.Where(item => item.Month <= month).ToList();
        var income = toDate.Sum(item => item.Income);
        var expenses = toDate.Sum(item => item.Expenses);
        var previousIncome = toDate.Sum(item => item.PreviousIncome);
        var previousExpenses = toDate.Sum(item => item.PreviousExpenses);
        var monthLabel = Capitalize(new DateTime(year, month, 1).ToString("MMMM 'de' yyyy", Culture));

        var monthFigures = new List<OwnerReportFigure>
        {
            new("Rendimentos do mês", current.Income, current.PreviousIncome, null),
            new("Gastos do mês", current.Expenses, current.PreviousExpenses, null),
            new("Resultado do mês", current.Result, current.PreviousResult, null),
        };
        var yearFigures = new List<OwnerReportFigure>
        {
            new($"Rendimentos até {MonthName(month)}", income, previousIncome, null),
            new($"Gastos até {MonthName(month)}", expenses, previousExpenses, null),
            new($"Resultado até {MonthName(month)}", income - expenses, previousIncome - previousExpenses, null),
        };

        var averageExpenses = toDate.Count == 0 ? 0m : decimal.Round(toDate.Average(item => item.Expenses), 2);
        decimal? coverage = averageExpenses > 0 ? decimal.Round(current.Cash / averageExpenses, 1) : null;
        var overdueCustomers = receivables.TotalBuckets.Skip(3).Sum();
        var position = new List<OwnerReportFigure>
        {
            new("Tesouraria no fim do mês", current.Cash, null, coverage is null ? null : string.Create(Culture, $"cobre {coverage:0.0} meses de gastos")),
            new("Clientes por receber", current.Receivables, null, overdueCustomers > 0 ? string.Create(Culture, $"{overdueCustomers:N2} € com mais de 90 dias") : null),
            new("Fornecedores por pagar", current.Payables, null, null),
        };

        var highlights = new List<string>
        {
            Sentence("Em", monthLabel.ToLower(Culture), current.Income, current.PreviousIncome, "os rendimentos foram de", "face ao mesmo mês do ano anterior"),
            string.Create(Culture, $"O resultado do mês foi de {Money(current.Result)} ({(current.Result >= 0 ? "lucro" : "prejuízo")}); até {MonthName(month)}, o resultado acumulado é de {Money(income - expenses)}, contra {Money(previousIncome - previousExpenses)} no mesmo período do ano anterior."),
            string.Create(Culture, $"No fim do mês, a tesouraria era de {Money(current.Cash)}, os clientes deviam {Money(current.Receivables)} e a empresa devia {Money(current.Payables)} a fornecedores."),
        };
        if (budgetReport is not null)
        {
            highlights.Add(string.Create(Culture,
                $"Face ao orçamento até {MonthName(month)}: rendimentos {Money(budgetReport.ActualIncome)} de {Money(budgetReport.BudgetIncome)} previstos; gastos {Money(budgetReport.ActualExpenses)} de {Money(budgetReport.BudgetExpenses)}."));
        }

        var alerts = new List<OwnerReportAlert>();
        if (current.Result < 0)
        {
            alerts.Add(new OwnerReportAlert("aviso", string.Create(Culture, $"O mês fechou com prejuízo de {Money(-current.Result)}.")));
        }

        if (current.PreviousIncome > 0 && current.Income < current.PreviousIncome * 0.8m)
        {
            alerts.Add(new OwnerReportAlert("aviso", string.Create(Culture,
                $"Os rendimentos do mês caíram {Percent(1 - (current.Income / current.PreviousIncome))} face ao mesmo mês do ano anterior (critério da aplicação: queda de mais de 20 %).")));
        }

        if (coverage is { } months && months < 1m)
        {
            alerts.Add(new OwnerReportAlert("aviso", string.Create(Culture,
                $"A tesouraria cobre menos de um mês de gastos ({months:0.0} meses, pela média dos gastos mensais do ano).")));
        }

        if (overdueCustomers > 0)
        {
            alerts.Add(new OwnerReportAlert("info", string.Create(Culture,
                $"Há {Money(overdueCustomers)} de clientes por receber há mais de 90 dias (contados da data do documento).")));
        }

        if (budgetReport is not null)
        {
            var unfavourable = budgetReport.Lines
                .Where(line => line.CostCenterCode is null && !line.Favourable && line.DeviationPercent is { } percent && Math.Abs(percent) >= 10m)
                .OrderBy(line => line.Deviation)
                .Take(3)
                .ToList();
            foreach (var line in unfavourable)
            {
                alerts.Add(new OwnerReportAlert("info", string.Create(Culture,
                    $"Orçamento: {line.AccountCode} {line.AccountName} com desvio desfavorável de {Money(Math.Abs(line.Deviation))} ({line.DeviationPercent:0.#} %).")));
            }
        }

        return new OwnerReport(year, month, monthLabel, monthFigures, yearFigures, position, highlights, alerts, evolution, receivables, payables, budgetReport);
    }

    private static string Sentence(string prefix, string period, decimal value, decimal comparative, string what, string against)
    {
        var change = comparative == 0m
            ? "sem valor no período homólogo para comparar"
            : string.Create(Culture, $"{(value >= comparative ? "mais" : "menos")} {Percent(Math.Abs((value - comparative) / comparative))} {against}");
        return string.Create(Culture, $"{prefix} {period}, {what} {Money(value)}, {change}.");
    }

    private static string Money(decimal value) => string.Create(Culture, $"{value:N2} €");

    private static string Percent(decimal ratio) => string.Create(Culture, $"{ratio * 100m:0.#} %");

    private static string MonthName(int month) => Culture.DateTimeFormat.GetMonthName(month);

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpper(value[0], Culture) + value[1..];
}
