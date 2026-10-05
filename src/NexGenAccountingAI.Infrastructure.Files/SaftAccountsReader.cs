using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NexGenAccountingAI.Application.Saft;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Lê o bloco <c>MasterFiles/GeneralLedgerAccounts</c> de um ficheiro SAF-T (PT) com
/// <see cref="XmlReader"/>, DTD proibido e resolução externa desativada. Materializa
/// apenas esse subárvore e para antes de <c>GeneralLedgerEntries</c>; o resto do
/// ficheiro não é carregado. Um ficheiro sem o bloco devolve lista vazia.
/// </summary>
public sealed class SaftAccountsReader : ISaftAccountsReader
{
    static SaftAccountsReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IReadOnlyList<SaftAccount>> ReadAsync(Stream content, CancellationToken cancellationToken = default)
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

        try
        {
            using var reader = XmlReader.Create(content, settings);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Os movimentos vêm depois das contas; se lá chegámos, não há contas.
                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName.Equals("GeneralLedgerEntries", StringComparison.OrdinalIgnoreCase))
                {
                    return [];
                }

                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName.Equals("GeneralLedgerAccounts", StringComparison.OrdinalIgnoreCase))
                {
                    using var subtree = reader.ReadSubtree();
                    var accounts = await XElement
                        .LoadAsync(subtree, LoadOptions.None, cancellationToken)
                        .ConfigureAwait(false);
                    return Build(accounts);
                }
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }

        return [];
    }

    private static List<SaftAccount> Build(XElement accounts)
    {
        var result = new List<SaftAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taxonomyReference = accounts.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("TaxonomyReference", StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim().ToUpperInvariant();
        if (taxonomyReference is not ("S" or "M" or "N" or "O"))
        {
            taxonomyReference = null;
        }

        foreach (var account in accounts.Elements().Where(element =>
            element.Name.LocalName.Equals("Account", StringComparison.OrdinalIgnoreCase)))
        {
            string? Field(string name) => account
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim();

            var code = Field("AccountID");
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code))
            {
                continue;
            }

            result.Add(new SaftAccount(
                code,
                Field("AccountDescription") ?? string.Empty,
                ParseAmount(Field("OpeningDebitBalance")),
                ParseAmount(Field("OpeningCreditBalance")),
                Field("GroupingCategory"),
                taxonomyReference is not null &&
                int.TryParse(Field("TaxonomyCode"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var taxonomyCode) && taxonomyCode > 0
                    ? taxonomyCode
                    : null,
                taxonomyReference));
        }

        return result;
    }

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : 0m;
}
