using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma linha do quadro 04: um adquirente e um tipo de operação.</summary>
/// <param name="Prefix">O prefixo do Estado-Membro (coluna 2): o código do país, com <c>EL</c> para a Grécia.</param>
/// <param name="Value">O valor arredondado ao euro (coluna 4).</param>
/// <param name="ExactValue">O valor antes do arredondamento.</param>
/// <param name="OperationType">1 transmissões de bens, 4 operações triangulares, 5 prestações de serviços (coluna 5).</param>
public sealed record RecapitulativeLine(
    string Prefix, string TaxNumber, string PartyName, decimal Value, decimal ExactValue, int OperationType);

/// <summary>Um movimento de uma operação intracomunitária que não pôde entrar no quadro 04, e porquê.</summary>
public sealed record RecapitulativeIssue(string EntryReference, DateOnly EntryDate, string AccountCode, decimal Amount, string Reason);

/// <param name="Period">O período (<c>03</c>, <c>06T</c>…), ou <c>null</c> se as datas não formam um mês nem um trimestre.</param>
/// <param name="Field10">Soma das operações de tipo 1.</param>
/// <param name="Field17">Soma das operações de tipo 4.</param>
/// <param name="Field18">Soma das operações de tipo 5.</param>
/// <param name="Field19">O valor que deve coincidir com o campo 7 da declaração periódica (tipos 1 e 5).</param>
public sealed record RecapitulativeStatement(
    DateOnly From,
    DateOnly To,
    string? Period,
    IReadOnlyList<RecapitulativeLine> Lines,
    decimal Field10,
    decimal Field17,
    decimal Field18,
    decimal Field19,
    IReadOnlyList<RecapitulativeIssue> Issues,
    string Source,
    string Disclaimer);

/// <summary>
/// Prepara os quadros 04 e 05 da declaração recapitulativa de IVA: as
/// operações do período cuja natureza de IVA (do código da linha ou, sem
/// código, da classificação da conta) é transmissão intracomunitária de bens,
/// operação triangular ou prestação de serviços intracomunitária, agregadas
/// por adquirente e tipo. O adquirente é o terceiro da linha ou, se a linha
/// não o tem, o único terceiro do lançamento. Só lê; exclui os anulados. Os
/// movimentos sem adquirente identificado, ou com um adquirente que não é de
/// outro Estado-Membro, ficam de fora e são listados.
/// </summary>
public sealed class RecapitulativeStatementService(AccountingDbContext context)
{
    public const string Source =
        "Autoridade Tributária e Aduaneira — modelo da declaração recapitulativa e instruções de preenchimento, consultados em 2026-10-04.";

    public const string Disclaimer =
        "Mapa de apoio ao preenchimento, não validado pela AT. Não trata as vendas de meios de transporte novos a particulares (campo 11) " +
        "nem as transferências de bens à consignação (quadro 06), e não confirma os números de identificação no VIES.";

    private static readonly VatOperationNature[] Natures =
    [
        VatOperationNature.TransmissoesIntracomunitariasBens,
        VatOperationNature.OperacoesTriangulares,
        VatOperationNature.PrestacoesServicosIntracomunitarias,
    ];

    // Os Estados-Membros da União, pelo código do país; a Irlanda do Norte (XI) só para bens.
    private static readonly HashSet<string> MemberStates = new(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GR", "HR", "HU", "IE", "IT",
        "LT", "LU", "LV", "MT", "NL", "PL", "RO", "SE", "SI", "SK",
    };

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<RecapitulativeStatement?> BuildAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var hasAccess = await context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasAccess)
        {
            return null;
        }

        var lines = context.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId &&
                line.JournalEntry.Status != JournalEntryStatus.Voided &&
                line.JournalEntry.EntryDate >= from &&
                line.JournalEntry.EntryDate <= to &&
                !line.Account.Code.StartsWith("243"));

        // O código de IVA da linha prevalece; sem código vale a classificação da conta.
        var byCode = await lines
            .Where(line => line.VatCodeId != null && Natures.Contains(line.VatCode!.Nature))
            .Select(line => new Movement(
                line.JournalEntryId, line.JournalEntry.Reference, line.JournalEntry.EntryDate, line.Account.Code,
                line.PartyId, line.VatCode!.Nature, line.Credit - line.Debit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byAccount = await lines
            .Where(line => line.VatCodeId == null)
            .Join(
                context.AccountVatClassifications.AsNoTracking().Where(item => item.CompanyId == companyId && Natures.Contains(item.Nature)),
                line => line.AccountId,
                classification => classification.AccountId,
                (line, classification) => new Movement(
                    line.JournalEntryId, line.JournalEntry.Reference, line.JournalEntry.EntryDate, line.Account.Code,
                    line.PartyId, classification.Nature, line.Credit - line.Debit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var movements = byCode.Concat(byAccount).Where(item => item.Amount != 0m).ToList();

        // Uma linha de venda sem terceiro herda o do lançamento, se for um só.
        var entryIds = movements.Where(item => item.PartyId is null).Select(item => item.EntryId).Distinct().ToList();
        var entryParties = entryIds.Count == 0
            ? []
            : (await context.JournalEntryLines
                    .AsNoTracking()
                    .Where(line => line.CompanyId == companyId && entryIds.Contains(line.JournalEntryId) && line.PartyId != null)
                    .Select(line => new { line.JournalEntryId, PartyId = line.PartyId!.Value })
                    .Distinct()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .GroupBy(item => item.JournalEntryId)
                .ToDictionary(group => group.Key, group => group.Select(item => item.PartyId).ToList());

        var partyIds = movements
            .Select(item => item.PartyId)
            .OfType<Guid>()
            .Concat(entryParties.Values.SelectMany(ids => ids))
            .Distinct()
            .ToList();
        var parties = await context.Parties
            .AsNoTracking()
            .Where(party => party.CompanyId == companyId && partyIds.Contains(party.Id))
            .ToDictionaryAsync(party => party.Id, party => new { party.CountryCode, party.TaxRegistrationNumber, party.Name }, cancellationToken)
            .ConfigureAwait(false);

        var issues = new List<RecapitulativeIssue>();
        var totals = new Dictionary<(string Prefix, string TaxNumber, int Type), (string Name, decimal Amount)>();
        foreach (var movement in movements)
        {
            void Issue(string reason) =>
                issues.Add(new RecapitulativeIssue(movement.Reference, movement.Date, movement.AccountCode, movement.Amount, reason));

            var partyId = movement.PartyId;
            if (partyId is null)
            {
                var candidates = entryParties.GetValueOrDefault(movement.EntryId);
                if (candidates is not { Count: 1 })
                {
                    Issue(candidates is null ? "O lançamento não tem terceiro: não se sabe quem é o adquirente." : "O lançamento tem mais de um terceiro: indique o adquirente na linha.");
                    continue;
                }

                partyId = candidates[0];
            }

            var party = parties[partyId.Value];
            var type = movement.Nature switch
            {
                VatOperationNature.OperacoesTriangulares => 4,
                VatOperationNature.PrestacoesServicosIntracomunitarias => 5,
                _ => 1,
            };
            var country = party.CountryCode.ToUpperInvariant();
            // A Irlanda do Norte só conta para bens (tipos 1 e 4); os serviços vão ao campo 8 da declaração periódica.
            if (!(MemberStates.Contains(country) || (country == "XI" && type != 5)))
            {
                Issue($"O adquirente {party.Name} é de {country}, que não é outro Estado-Membro: a operação não vai à declaração recapitulativa.");
                continue;
            }

            var prefix = country == "GR" ? "EL" : country;
            var taxNumber = party.TaxRegistrationNumber.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            if (taxNumber.StartsWith(prefix, StringComparison.Ordinal) || taxNumber.StartsWith(country, StringComparison.Ordinal))
            {
                taxNumber = taxNumber[2..];
            }

            if (taxNumber.Length == 0)
            {
                Issue($"O adquirente {party.Name} não tem número de identificação fiscal.");
                continue;
            }

            var key = (prefix, taxNumber, type);
            totals[key] = (party.Name, totals.GetValueOrDefault(key).Amount + movement.Amount);
        }

        var statementLines = totals
            .Where(item => item.Value.Amount != 0m)
            .Select(item => new RecapitulativeLine(
                item.Key.Prefix, item.Key.TaxNumber, item.Value.Name,
                decimal.Round(item.Value.Amount, 0, MidpointRounding.AwayFromZero), item.Value.Amount, item.Key.Type))
            .OrderBy(line => line.Prefix, StringComparer.Ordinal)
            .ThenBy(line => line.TaxNumber, StringComparer.Ordinal)
            .ThenBy(line => line.OperationType)
            .ToList();
        decimal Sum(int type) => statementLines.Where(line => line.OperationType == type).Sum(line => line.Value);

        return new RecapitulativeStatement(
            from, to, VatDeclarationService.PeriodOf(from, to), statementLines,
            Sum(1), Sum(4), Sum(5), Sum(1) + Sum(5),
            issues.OrderBy(issue => issue.EntryDate).ThenBy(issue => issue.EntryReference, StringComparer.Ordinal).ToList(),
            Source, Disclaimer);
    }

    private sealed record Movement(
        Guid EntryId, string Reference, DateOnly Date, string AccountCode, Guid? PartyId, VatOperationNature Nature, decimal Amount);
}
