using System.Text;
using System.Xml;
using System.Xml.Linq;
using AccountingAI.Application.Saft;

namespace AccountingAI.Infrastructure.Files;

/// <summary>
/// Lê os elementos <c>Customer</c> e <c>Supplier</c> de <c>MasterFiles</c> de um
/// ficheiro SAF-T (PT) com <see cref="XmlReader"/>, DTD proibido e resolução
/// externa desativada. Materializa um elemento de cada vez e para antes de
/// <c>GeneralLedgerEntries</c> e <c>SourceDocuments</c>.
/// </summary>
public sealed class SaftPartiesReader : ISaftPartiesReader
{
    static SaftPartiesReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IReadOnlyList<SaftParty>> ReadAsync(Stream content, CancellationToken cancellationToken = default)
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

        var result = new List<SaftParty>();

        try
        {
            using var reader = XmlReader.Create(content, settings);
            var inMasterFiles = false;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (Is(reader, "GeneralLedgerEntries") || Is(reader, "SourceDocuments"))
                {
                    break;
                }

                if (Is(reader, "MasterFiles"))
                {
                    inMasterFiles = true;
                    continue;
                }

                var isCustomer = Is(reader, "Customer");
                if (!inMasterFiles || reader.Depth != 2 || !(isCustomer || Is(reader, "Supplier")))
                {
                    continue;
                }

                using var subtree = reader.ReadSubtree();
                var element = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                result.Add(Build(element, isCustomer ? SaftPartyKind.Customer : SaftPartyKind.Supplier));
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }

        return result;
    }

    private static bool Is(XmlReader reader, string name) =>
        reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static SaftParty Build(XElement element, SaftPartyKind kind)
    {
        static XElement? Child(XElement parent, string name) => parent
            .Elements()
            .FirstOrDefault(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

        string? Field(string name) => Child(element, name)?.Value.Trim();

        var prefix = kind == SaftPartyKind.Customer ? "Customer" : "Supplier";
        var billingAddress = Child(element, "BillingAddress");
        var country = billingAddress is null ? null : Child(billingAddress, "Country")?.Value.Trim();
        var accountId = Field("AccountID");

        return new SaftParty(
            kind,
            Field(prefix + "ID") ?? string.Empty,
            string.IsNullOrWhiteSpace(accountId) ? null : accountId,
            Field(prefix + "TaxID") ?? string.Empty,
            Field("CompanyName") ?? string.Empty,
            string.IsNullOrWhiteSpace(country) ? null : country);
    }
}
