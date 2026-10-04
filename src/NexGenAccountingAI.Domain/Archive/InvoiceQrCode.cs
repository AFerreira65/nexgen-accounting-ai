using System.Globalization;

namespace NexGenAccountingAI.Domain.Archive;

/// <summary>A base e o IVA de um espaço fiscal (PT, PT-AC, PT-MA) do código QR.</summary>
public sealed record InvoiceQrTaxRegion(
    string Region,
    decimal ExemptBase,
    decimal ReducedBase,
    decimal ReducedVat,
    decimal IntermediateBase,
    decimal IntermediateVat,
    decimal NormalBase,
    decimal NormalVat)
{
    public decimal TaxableBase => ExemptBase + ReducedBase + IntermediateBase + NormalBase;

    public decimal Vat => ReducedVat + IntermediateVat + NormalVat;
}

/// <summary>
/// O conteúdo do código QR de uma fatura portuguesa, lido pelas «Especificações
/// Técnicas — Código de Barras Bidimensional — Código QR» da AT (versão 1.0,
/// agosto de 2020, Portaria n.º 195/2020): campos <c>código:valor</c> separados
/// por <c>*</c>, valores monetários com ponto decimal. A — NIF do emitente;
/// B — NIF do adquirente; C — país; D — tipo; E — estado; F — data (AAAAMMDD);
/// G — identificação do documento; H — ATCUD; I1–I8, J1–J8, K1–K8 — espaços
/// fiscais; L — não sujeito; M — imposto do selo; N — total de impostos;
/// O — total do documento; P — retenções; Q — hash; R — certificado;
/// S — outras informações.
/// </summary>
public sealed record InvoiceQrCode(
    string IssuerTaxId,
    string? BuyerTaxId,
    string? BuyerCountry,
    string? DocumentType,
    string? Status,
    DateOnly? DocumentDate,
    string DocumentNo,
    string? Atcud,
    IReadOnlyList<InvoiceQrTaxRegion> Regions,
    decimal NotSubject,
    decimal StampDuty,
    decimal TaxTotal,
    decimal GrossTotal,
    decimal Withholding,
    string? Hash,
    string? CertificateNumber,
    string? OtherInformation,
    IReadOnlyList<string> Warnings)
{
    public const int MaxLength = 1000;

    /// <summary>O total sem impostos: o total do documento menos o IVA e o imposto do selo.</summary>
    public decimal NetTotal => GrossTotal - TaxTotal;

    public decimal Vat => Regions.Sum(region => region.Vat);

    public bool IsCreditNote => string.Equals(DocumentType, "NC", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lê a mensagem do código QR. Recusa um texto que não tenha o NIF do
    /// emitente (A), a identificação do documento (G) e o total (O); os outros
    /// campos obrigatórios em falta ficam em <see cref="Warnings"/>.
    /// </summary>
    public static InvoiceQrCode Parse(string? text)
    {
        var value = (text ?? string.Empty).Trim().Trim('﻿');
        if (value.Length == 0)
        {
            throw new FormatException("O código QR está vazio.");
        }

        if (value.Length > MaxLength)
        {
            throw new FormatException($"O código QR tem mais de {MaxLength} caracteres.");
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split('*'))
        {
            var separator = part.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            fields.TryAdd(part[..separator].Trim(), part[(separator + 1)..].Trim());
        }

        string? Field(string code) => fields.TryGetValue(code, out var found) && found.Length > 0 ? found : null;

        var issuer = Field("A");
        var number = Field("G");
        var grossText = Field("O");
        if (issuer is null || number is null || grossText is null)
        {
            throw new FormatException("O texto não é o código QR de uma fatura: faltam o NIF do emitente (A), o documento (G) ou o total (O).");
        }

        var warnings = new List<string>();
        decimal Amount(string code)
        {
            var text = Field(code);
            if (text is null)
            {
                return 0m;
            }

            if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
            {
                return amount;
            }

            warnings.Add($"O campo {code} não é um valor ('{text}').");
            return 0m;
        }

        foreach (var code in new[] { "B", "C", "D", "E", "F", "H", "I1", "N", "Q", "R" })
        {
            if (Field(code) is null)
            {
                warnings.Add($"Falta o campo obrigatório {code}.");
            }
        }

        DateOnly? date = null;
        if (Field("F") is { } dateText)
        {
            if (DateOnly.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = parsed;
            }
            else
            {
                warnings.Add($"A data (F) não está no formato AAAAMMDD ('{dateText}').");
            }
        }

        var regions = new List<InvoiceQrTaxRegion>();
        foreach (var letter in new[] { "I", "J", "K" })
        {
            var region = Field(letter + "1");
            if (region is null)
            {
                continue;
            }

            regions.Add(new InvoiceQrTaxRegion(
                region, Amount(letter + "2"), Amount(letter + "3"), Amount(letter + "4"), Amount(letter + "5"),
                Amount(letter + "6"), Amount(letter + "7"), Amount(letter + "8")));
        }

        return new InvoiceQrCode(
            issuer,
            Field("B"),
            Field("C"),
            Field("D")?.ToUpperInvariant(),
            Field("E")?.ToUpperInvariant(),
            date,
            number,
            Field("H"),
            regions,
            Amount("L"),
            Amount("M"),
            Amount("N"),
            Amount("O"),
            Amount("P"),
            Field("Q"),
            Field("R"),
            Field("S"),
            warnings);
    }
}
