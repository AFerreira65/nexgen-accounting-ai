using System.Globalization;
using NexGenAccountingAI.Application.ManagementReports;
using NexGenAccountingAI.Application.Maps;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O texto que a IA redigiu para uma secção, ou o motivo de não o ter feito.</summary>
public sealed record ReportPackNarration(IReadOnlyList<string> Paragraphs, string? Unavailable, string Model);

/// <summary>
/// Compõe um relato (F4B-04) a partir do que o utilizador pede: por cada secção, corre
/// o mapa, o balancete ou os rácios e formata-os em tabelas; o texto vai tal como está.
/// A redação com IA é à parte (<see cref="NarrateAsync"/>): o utilizador vê o texto,
/// corrige-o e só depois o relato é gerado.
/// </summary>
public sealed class ReportPackService(
    AccountingDbContext context,
    FinancialMapService maps,
    TrialBalanceTreeService balancete,
    FinancialStatementsService statements,
    ManagementReportService managementReports,
    TimeProvider clock)
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-PT");
    private static readonly string[] Months =
        ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    /// <exception cref="ArgumentException">O pedido não é válido (mensagem em português).</exception>
    public async Task<ReportPack?> BuildAsync(Guid userId, Guid companyId, ReportPackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("O relato precisa de um título.", nameof(request));
        }

        if (request.FromMonth is < 1 or > 12 || request.ToMonth is < 1 or > 12 || request.ToMonth < request.FromMonth || request.Year is < 1990 or > 2100)
        {
            throw new ArgumentException("O período tem de ser de um mês a outro do mesmo exercício.", nameof(request));
        }

        var company = await context.Companies
            .AsNoTracking()
            .Where(item => item.Id == companyId && item.IsActive && item.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive))
            .Select(item => new { item.LegalName, item.TaxRegistrationNumber })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return null;
        }

        var period = new MapRequest(request.Year, request.FromMonth, request.ToMonth, request.IncludeClosing);
        var sections = new List<ReportPackSection>();
        var notice = false;
        foreach (var section in request.Sections.Where(section => section.Enabled))
        {
            switch (section.Kind?.Trim().ToLowerInvariant())
            {
                case "texto":
                    sections.Add(new ReportPackSection(section.Title ?? "Texto", SplitParagraphs(section.Text)));
                    notice |= section.Text?.Contains("[IA]", StringComparison.Ordinal) == true;
                    break;
                case "mapa":
                    sections.Add(await MapSectionAsync(userId, companyId, section, period, cancellationToken).ConfigureAwait(false));
                    break;
                case "balancete":
                    sections.Add(await BalanceteSectionAsync(companyId, section, request, cancellationToken).ConfigureAwait(false));
                    break;
                case "racios":
                    sections.Add(await RatiosSectionAsync(userId, companyId, section, request, cancellationToken).ConfigureAwait(false));
                    break;
                default:
                    throw new ArgumentException($"Não existe o tipo de secção «{section.Kind}».", nameof(request));
            }
        }

        if (sections.Count == 0)
        {
            throw new ArgumentException("O relato não tem secções.", nameof(request));
        }

        return new ReportPack(
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim(),
            company.LegalName,
            company.TaxRegistrationNumber,
            PeriodLabel(request.Year, request.FromMonth, request.ToMonth),
            clock.GetUtcNow(),
            sections,
            string.IsNullOrWhiteSpace(request.Preparer) ? null : request.Preparer.Trim(),
            notice ? ManagementReportResult.Notice : null);
    }

    /// <summary>
    /// Pede à IA um texto para uma secção, a partir dos números do período (o dossier dos
    /// relatórios de gestão). Não grava nada no relato: devolve os parágrafos para o
    /// utilizador rever. <c>null</c> sem acesso à empresa.
    /// </summary>
    public async Task<ReportPackNarration?> NarrateAsync(
        Guid userId, Guid companyId, string sectionTitle, string? instructions, int year, int fromMonth, int toMonth, CancellationToken cancellationToken = default)
    {
        var from = new DateOnly(year, fromMonth, 1);
        var to = new DateOnly(year, toMonth, DateTime.DaysInMonth(year, toMonth));
        var question =
            $"Redige, em português de Portugal e em prosa corrida (três a cinco parágrafos, sem listas nem títulos), " +
            $"a secção «{sectionTitle}» de um relatório económico-financeiro que a empresa vai entregar ao banco para apreciação de crédito. " +
            "Descreve a dimensão e a evolução da atividade, a rendibilidade, a estrutura financeira e a liquidez a partir dos números, " +
            "com os valores mais relevantes em euros, num tom factual e profissional, sem recomendações ao banco. " +
            (string.IsNullOrWhiteSpace(instructions) ? string.Empty : $"Indicações do contabilista: {instructions.Trim()}");
        var generation = await managementReports
            .GenerateAsync(userId, companyId, new ManagementReportRequest(null, question, from, to), cancellationToken)
            .ConfigureAwait(false);
        if (generation.NotFound)
        {
            return null;
        }

        if (generation.Disabled || generation.View?.Result is null)
        {
            return new ReportPackNarration([], generation.DisabledReason ?? "A IA não devolveu texto.", "—");
        }

        var result = generation.View.Result;
        var paragraphs = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            paragraphs.Add(result.Summary.Trim());
        }

        paragraphs.AddRange(result.Sections.SelectMany(section => SplitParagraphs(section.Body)));
        return new ReportPackNarration(paragraphs, null, generation.View.Model);
    }

    private async Task<ReportPackSection> MapSectionAsync(Guid userId, Guid companyId, ReportPackSectionRequest section, MapRequest period, CancellationToken cancellationToken)
    {
        MapDefinition definition;
        string title;
        if (section.MapId is { } id)
        {
            var saved = await maps.GetAsync(userId, companyId, id, cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException("O mapa guardado já não existe.", nameof(section));
            definition = saved.Definition;
            title = saved.Name;
        }
        else
        {
            var template = MapTemplates.Find(section.TemplateKey)
                ?? throw new ArgumentException($"Não existe o modelo de mapa «{section.TemplateKey}».", nameof(section));
            definition = template.Definition;
            title = template.Name;
        }

        var result = await maps.RunAsync(userId, companyId, definition, period, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("Sem acesso ao mapa.", nameof(section));
        var header = new List<string> { "Rubrica" };
        header.AddRange(result.Columns.Select(column => column.Label));
        var numeric = new List<bool> { false };
        numeric.AddRange(result.Columns.Select(_ => true));
        var rows = result.Rows
            .Select(row => new ReportPackRow(
                [row.Label, .. row.Values.Select((value, index) => Format(value, result.Columns[index].Percent))],
                row.Level,
                row.Bold,
                row.Kind == nameof(MapRowKind.Title)))
            .ToList();
        var notes = result.Warnings.Count > 0 ? result.Warnings : null;
        return new ReportPackSection(section.Title ?? title, SplitParagraphs(section.Text), new ReportPackTable(header, rows, numeric), notes);
    }

    private async Task<ReportPackSection> BalanceteSectionAsync(Guid companyId, ReportPackSectionRequest section, ReportPackRequest request, CancellationToken cancellationToken)
    {
        var view = await balancete.RunAsync(
            companyId,
            new TrialBalanceTreeQuery(request.Year, request.FromMonth, request.ToMonth, request.IncludeClosing, section.ColumnSet, Depth: Math.Max(1, section.Depth ?? 2), Everything: true),
            cancellationToken).ConfigureAwait(false);
        var header = new List<string> { "Conta", "Descrição" };
        header.AddRange(view.Columns.Select(column => column.Label));
        var numeric = new List<bool> { false, false };
        numeric.AddRange(view.Columns.Select(_ => true));
        var rows = view.Rows
            .Select(row => new ReportPackRow([row.Code, row.Name, .. row.Values.Select((value, index) => Format(value, view.Columns[index].Percent))], row.Depth - 1, row.Depth == 1))
            .ToList();
        rows.Add(new ReportPackRow(["", "Totais", .. view.Totals.Select((value, index) => Format(value, view.Columns[index].Percent))], 0, true));
        return new ReportPackSection(section.Title ?? "Balancete", SplitParagraphs(section.Text), new ReportPackTable(header, rows, numeric), view.Warnings.Count > 0 ? view.Warnings : null);
    }

    private async Task<ReportPackSection> RatiosSectionAsync(Guid userId, Guid companyId, ReportPackSectionRequest section, ReportPackRequest request, CancellationToken cancellationToken)
    {
        var from = new DateOnly(request.Year, request.FromMonth, 1);
        var to = new DateOnly(request.Year, request.ToMonth, DateTime.DaysInMonth(request.Year, request.ToMonth));
        var ratios = await statements.RatiosAsync(userId, companyId, from, to, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("Sem acesso aos rácios.", nameof(section));
        var rows = new List<ReportPackRow>();
        foreach (var group in ratios.Ratios.GroupBy(ratio => ratio.Group))
        {
            rows.Add(new ReportPackRow([group.Key, "", "", ""], 0, true, true));
            rows.AddRange(group.Select(ratio => new ReportPackRow([ratio.Label, FormatRatio(ratio.Value, ratio.Unit), FormatRatio(ratio.Comparative, ratio.Unit), ratio.Formula], 1)));
        }

        var notes = new List<string>();
        if (ratios.BalanceDifference != 0m)
        {
            notes.Add($"O balanço de base tem uma diferença de {ratios.BalanceDifference.ToString("N2", Portuguese)} €; os rácios devem ser lidos com reserva.");
        }

        if (ratios.ExcludedAccounts > 0)
        {
            notes.Add($"{ratios.ExcludedAccounts} conta(s) com saldo ficaram fora das demonstrações de base.");
        }

        return new ReportPackSection(
            section.Title ?? "Rácios económico-financeiros",
            SplitParagraphs(section.Text),
            new ReportPackTable(["Rácio", "Período", "Período homólogo", "Fórmula"], rows, [false, true, true, false]),
            notes.Count > 0 ? notes : null);
    }

    /// <summary>Como no ecrã: o zero é um traço, para os valores que contam sobressaírem.</summary>
    private static string Format(decimal? value, bool percent) =>
        value is null ? string.Empty
        : decimal.Round(value.Value, 2) == 0m ? "–"
        : percent ? $"{value.Value.ToString("N1", Portuguese)} %"
        : value.Value.ToString("N2", Portuguese);

    private static string FormatRatio(decimal? value, string unit) => value is null ? "—" : unit switch
    {
        "%" => $"{value.Value.ToString("N1", Portuguese)} %",
        "dias" => $"{value.Value.ToString("N0", Portuguese)} dias",
        "€" => value.Value.ToString("N2", Portuguese),
        _ => value.Value.ToString("N2", Portuguese),
    };

    private static IReadOnlyList<string> SplitParagraphs(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(paragraph => paragraph.Replace('\n', ' '))
                .ToList();

    private static string PeriodLabel(int year, int fromMonth, int toMonth) =>
        fromMonth == 1 && toMonth == 12 ? $"Exercício de {year}"
        : fromMonth == toMonth ? $"{Capital(Months[fromMonth - 1])} de {year}"
        : $"{Capital(Months[fromMonth - 1])} a {Months[toMonth - 1]} de {year}";

    private static string Capital(string word) => char.ToUpper(word[0], Portuguese) + word[1..];
}
