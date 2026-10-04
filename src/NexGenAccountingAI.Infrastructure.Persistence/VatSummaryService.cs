using NexGenAccountingAI.Application.Vat;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Uma linha do resumo de IVA: o movimento de um período das contas com uma
/// dada classificação, agregado por natureza/taxa/região. <see cref="Kind"/> ∈
/// <c>base | imposto | outras</c> — <c>base</c> para as contas 6/7, <c>imposto</c>
/// para as <c>243*</c>. <see cref="Field"/> é o campo numerado da declaração
/// periódica sugerido pelo <see cref="VatDeclarationFieldCatalog"/> — best-effort,
/// <c>null</c> quando o catálogo não tem uma correspondência inequívoca.
/// </summary>
public sealed record VatSummaryRow(
    string Kind,
    string Nature,
    string Rate,
    string Region,
    decimal Debit,
    decimal Credit,
    decimal Net,
    int AccountCount,
    VatDeclarationField? Field);

/// <summary>
/// Uma conta relevante (movimento de classe 6/7 ou <c>243*</c>) com movimento no
/// período mas sem <see cref="AccountVatClassification"/> — fica de fora do
/// resumo; ver <see cref="VatSummary.Unclassified"/>.
/// </summary>
public sealed record VatSummaryUnclassifiedRow(
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    decimal Net);

/// <summary>
/// O resumo de IVA de um período, pronto a transcrever para a declaração periódica.
/// <see cref="FieldMappingSource"/>/<see cref="FieldMappingDisclaimer"/> acompanham
/// sempre os dados — ver <see cref="VatDeclarationFieldCatalog"/>.
/// </summary>
public sealed record VatSummary(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<VatSummaryRow> Rows,
    IReadOnlyList<VatSummaryUnclassifiedRow> Unclassified,
    string FieldMappingSource,
    string FieldMappingDisclaimer);

/// <summary>
/// Soma o movimento do período agrupado por natureza/taxa/região e separado em
/// base e imposto (<c>243*</c>) — a desagregação que se transcreve para a
/// declaração periódica. Cada linha é lida pelo seu código de IVA
/// (<see cref="VatCode"/>) quando o tem; sem código, pela classificação da sua
/// conta (<see cref="AccountVatClassification"/>). Só conta o que teve
/// movimento no período, pelo que as contas agregadas sem lançamentos próprios
/// não aparecem. Devolve também as contas relevantes com movimento no período
/// mas ainda sem classificação, para o resumo nunca parecer completo estando a
/// omitir contas por classificar. Só lê; exclui os lançamentos anulados. Não é
/// a declaração periódica oficial da AT.
/// </summary>
public sealed class VatSummaryService(AccountingDbContext context)
{
    public async Task<VatSummary?> BuildAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var parts = await PartsAsync(companyId, from, to, cancellationToken).ConfigureAwait(false);

        var grouped = parts
            .GroupBy(part => (part.Kind, part.Nature, part.Rate, part.Region))
            .Select(group => new
            {
                group.Key.Kind,
                group.Key.Nature,
                group.Key.Rate,
                group.Key.Region,
                Debit = group.Sum(part => part.Debit),
                Credit = group.Sum(part => part.Credit),
                AccountCount = group.Select(part => part.AccountId).Distinct().Count(),
            })
            .ToList();

        var rows = grouped
            .Select(row => new VatSummaryRow(
                row.Kind,
                row.Nature.ToString(),
                row.Rate.ToString(),
                row.Region.ToString(),
                decimal.Round(row.Debit, 2),
                decimal.Round(row.Credit, 2),
                decimal.Round(row.Debit - row.Credit, 2),
                row.AccountCount,
                VatDeclarationFieldCatalog.ForRow(row.Kind, row.Nature, row.Rate, row.Region)))
            .OrderBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Nature, StringComparer.Ordinal)
            .ThenBy(row => row.Rate, StringComparer.Ordinal)
            .ThenBy(row => row.Region, StringComparer.Ordinal)
            .ToList();

        var unclassifiedGrouped = await context.JournalEntryLines
            .AsNoTracking()
            .GroupJoin(
                context.AccountVatClassifications.AsNoTracking(),
                line => line.AccountId,
                classification => classification.AccountId,
                (line, classifications) => new { line, classifications })
            .SelectMany(
                item => item.classifications.DefaultIfEmpty(),
                (item, classification) => new { item.line, classification })
            .Where(item => item.line.CompanyId == companyId &&
                item.line.JournalEntry.Status != JournalEntryStatus.Voided &&
                item.line.JournalEntry.EntryDate >= from &&
                item.line.JournalEntry.EntryDate <= to &&
                item.classification == null &&
                item.line.VatCodeId == null &&
                !item.line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("2435")) &&
                item.line.Account.IsActive && item.line.Account.AllowsPosting &&
                (item.line.Account.Code.StartsWith("6") || item.line.Account.Code.StartsWith("7") ||
                 item.line.Account.Code.StartsWith("2432") || item.line.Account.Code.StartsWith("2433") || item.line.Account.Code.StartsWith("2434")))
            .GroupBy(item => new { item.line.Account.Code, item.line.Account.Name })
            .Select(group => new
            {
                group.Key.Code,
                group.Key.Name,
                Debit = group.Sum(item => item.line.Debit),
                Credit = group.Sum(item => item.line.Credit),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var unclassified = unclassifiedGrouped
            .Select(row => new VatSummaryUnclassifiedRow(
                row.Code,
                row.Name,
                decimal.Round(row.Debit, 2),
                decimal.Round(row.Credit, 2),
                decimal.Round(row.Debit - row.Credit, 2)))
            .OrderBy(row => row.AccountCode, StringComparer.Ordinal)
            .ToList();

        return new VatSummary(
            from, to, rows, unclassified,
            VatDeclarationFieldCatalog.Source, VatDeclarationFieldCatalog.Disclaimer);
    }

    /// <summary>
    /// O movimento do período por conta e por combinação natureza/taxa/região,
    /// das duas origens (código de IVA da linha ou classificação da conta). Não
    /// verifica o acesso: é para quem já o verificou.
    /// </summary>
    public async Task<IReadOnlyList<VatSummaryPart>> PartsAsync(
        Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        // Linhas sem código de IVA: valem pela classificação da sua conta (ADR-0009).
        var byAccount = await context.JournalEntryLines
            .AsNoTracking()
            .Join(
                context.AccountVatClassifications.AsNoTracking(),
                line => line.AccountId,
                classification => classification.AccountId,
                (line, classification) => new { line, classification })
            .Where(item => item.line.CompanyId == companyId &&
                item.classification.CompanyId == companyId &&
                item.line.VatCodeId == null &&
                !item.line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("2435")) &&
                item.line.JournalEntry.Status != JournalEntryStatus.Voided &&
                item.line.JournalEntry.EntryDate >= from &&
                item.line.JournalEntry.EntryDate <= to)
            .GroupBy(item => new
            {
                Kind = item.line.Account.Code.StartsWith("243")
                    ? "imposto"
                    : item.line.Account.Code.StartsWith("6") || item.line.Account.Code.StartsWith("7")
                        ? "base"
                        : "outras",
                item.classification.Nature,
                item.classification.Rate,
                item.classification.Region,
                item.line.AccountId,
            })
            .Select(group => new VatSummaryPart(
                group.Key.Kind,
                group.Key.Nature,
                group.Key.Rate,
                group.Key.Region,
                group.Key.AccountId,
                group.Sum(item => item.line.Debit),
                group.Sum(item => item.line.Credit)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Linhas com código de IVA: o código prevalece sobre a conta (ADR-0012).
        // Uma linha numa conta 243 é imposto; qualquer outra com código é base —
        // incluindo as compras em classe 3 e o imobilizado em classe 4.
        var byCode = await context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.VatCodeId != null &&
                !line.JournalEntry.Lines.Any(other => other.Account.Code.StartsWith("2435")) &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to)
            .GroupBy(line => new
            {
                Kind = line.Account.Code.StartsWith("243") ? "imposto" : "base",
                line.VatCode!.Nature,
                line.VatCode.Rate,
                line.VatCode.Region,
                line.AccountId,
            })
            .Select(group => new VatSummaryPart(
                group.Key.Kind,
                group.Key.Nature,
                group.Key.Rate,
                group.Key.Region,
                group.Key.AccountId,
                group.Sum(line => line.Debit),
                group.Sum(line => line.Credit)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. byAccount, .. byCode];
    }

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}

/// <summary>O movimento de uma conta numa combinação natureza/taxa/região, de uma das duas origens.</summary>
/// <param name="Kind"><c>base</c>, <c>imposto</c> (contas <c>243*</c>) ou <c>outras</c>.</param>
public sealed record VatSummaryPart(
    string Kind, VatOperationNature Nature, VatRate Rate, VatRegion Region, Guid AccountId, decimal Debit, decimal Credit);
