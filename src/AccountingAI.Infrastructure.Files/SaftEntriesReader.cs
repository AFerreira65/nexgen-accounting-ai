using System.Globalization;
using System.Text;
using System.Xml;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files;

/// <summary>
/// Lê um resumo do bloco <c>GeneralLedgerEntries</c> de um SAF-T (PT) totalmente em
/// streaming com <see cref="XmlReader"/> (DTD proibido, sem resolução externa,
/// Windows-1252 suportado): contagem de transações por diário, totais declarados e
/// intervalo de datas. Não materializa as linhas, pelo que suporta ficheiros
/// grandes. Um ficheiro sem o bloco devolve `HasEntries = false`.
/// </summary>
public sealed class SaftEntriesReader : ISaftEntriesReader
{
    static SaftEntriesReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SaftEntriesSummary> ReadSummaryAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = new XmlReaderSettings
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
            XmlResolver = null
        };

        var journals = new List<JournalAccumulator>();
        JournalAccumulator? currentJournal = null;
        var journalDescriptionPending = false;
        int? declaredEntryCount = null;
        decimal? totalDebit = null;
        decimal? totalCredit = null;
        var transactionCount = 0;
        DateOnly? fromDate = null;
        DateOnly? toDate = null;
        var inEntries = false;

        try
        {
            using var reader = XmlReader.Create(content, settings);
            var moved = await reader.ReadAsync().ConfigureAwait(false);
            while (moved)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.EndElement &&
                    reader.LocalName.Equals("GeneralLedgerEntries", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    moved = await reader.ReadAsync().ConfigureAwait(false);
                    continue;
                }

                // Alguns ramos consomem o conteúdo do elemento (que já deixa o leitor
                // no nó seguinte); nesse caso não se avança de novo no topo do ciclo.
                var consumed = false;
                switch (reader.LocalName)
                {
                    case "GeneralLedgerEntries":
                        inEntries = true;
                        break;
                    case "NumberOfEntries" when inEntries:
                        declaredEntryCount = ParseInt(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TotalDebit" when inEntries:
                        totalDebit = ParseDecimal(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "TotalCredit" when inEntries:
                        totalCredit = ParseDecimal(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        consumed = true;
                        break;
                    case "Journal" when inEntries:
                        currentJournal = null;
                        journalDescriptionPending = false;
                        break;
                    case "JournalID" when inEntries && currentJournal is null:
                        var code = (await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                        currentJournal = journals.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                        if (currentJournal is null)
                        {
                            currentJournal = new JournalAccumulator(code);
                            journals.Add(currentJournal);
                        }
                        journalDescriptionPending = currentJournal.Description.Length == 0;
                        consumed = true;
                        break;
                    case "Description" when inEntries && journalDescriptionPending && currentJournal is not null:
                        currentJournal.Description = (await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                        journalDescriptionPending = false;
                        consumed = true;
                        break;
                    case "Transaction" when inEntries:
                        journalDescriptionPending = false;
                        transactionCount++;
                        if (currentJournal is not null)
                        {
                            currentJournal.TransactionCount++;
                        }
                        break;
                    case "TransactionDate" when inEntries:
                        var parsed = ParseDate(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        if (parsed is { } date)
                        {
                            if (fromDate is null || date < fromDate) { fromDate = date; }
                            if (toDate is null || date > toDate) { toDate = date; }
                        }
                        consumed = true;
                        break;
                }

                moved = consumed
                    ? reader.ReadState == ReadState.Interactive
                    : await reader.ReadAsync().ConfigureAwait(false);
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }

        if (!inEntries)
        {
            return new SaftEntriesSummary(false, 0, null, null, null, null, null, []);
        }

        return new SaftEntriesSummary(
            true,
            transactionCount,
            declaredEntryCount,
            totalDebit,
            totalCredit,
            fromDate,
            toDate,
            journals.Select(item => new SaftJournalSummary(item.Code, item.Description, item.TransactionCount)).ToList());
    }

    private static int? ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;

    private sealed class JournalAccumulator(string code)
    {
        public string Code { get; } = code;
        public string Description { get; set; } = string.Empty;
        public int TransactionCount { get; set; }
    }
}
