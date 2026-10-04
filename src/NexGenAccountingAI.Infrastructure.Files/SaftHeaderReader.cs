using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Domain.Saft;

namespace NexGenAccountingAI.Infrastructure.Files;

/// <summary>
/// Lê o cabeçalho de um ficheiro SAF-T (PT) com <see cref="XmlReader"/>, DTD proibido
/// e resolução externa desativada. Só materializa o subárvore <c>Header</c>; o resto
/// do ficheiro não é carregado.
/// </summary>
public sealed class SaftHeaderReader : ISaftHeaderReader
{
    static SaftHeaderReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SaftHeader> ReadAsync(Stream content, CancellationToken cancellationToken = default)
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

                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName.Equals("Header", StringComparison.OrdinalIgnoreCase))
                {
                    using var subtree = reader.ReadSubtree();
                    var header = await XElement
                        .LoadAsync(subtree, LoadOptions.None, cancellationToken)
                        .ConfigureAwait(false);
                    return Build(header);
                }
            }
        }
        catch (XmlException exception)
        {
            throw new SaftFileFormatException($"O ficheiro não é um XML SAF-T legível: {exception.Message}");
        }

        throw new SaftFileFormatException("Não foi encontrado o elemento Header no ficheiro SAF-T.");
    }

    private static SaftHeader Build(XElement header)
    {
        string? Field(string name) => header
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();

        try
        {
            return new SaftHeader(
                Field("AuditFileVersion")!,
                Field("TaxRegistrationNumber")!,
                Field("TaxAccountingBasis")!,
                Field("CompanyName")!,
                ParseYear(Field("FiscalYear")),
                ParseDate(Field("StartDate"), "StartDate"),
                ParseDate(Field("EndDate"), "EndDate"),
                string.IsNullOrWhiteSpace(Field("CurrencyCode")) ? "EUR" : Field("CurrencyCode")!,
                businessName: Field("BusinessName"),
                dateCreated: TryParseDate(Field("DateCreated")),
                taxEntity: Field("TaxEntity"),
                productId: Field("ProductID"));
        }
        catch (Exception exception)
            when (exception is ArgumentException or ArgumentOutOfRangeException or FormatException)
        {
            throw new SaftFileFormatException($"O cabeçalho do SAF-T é inválido: {exception.Message}");
        }
    }

    private static int ParseYear(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            throw new FormatException($"FiscalYear ausente ou inválido: '{value}'.");
        }

        return year;
    }

    private static DateOnly ParseDate(string? value, string field)
    {
        if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new FormatException($"{field} ausente ou inválida: '{value}'.");
        }

        return date;
    }

    private static DateOnly? TryParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
