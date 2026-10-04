namespace NexGenAccountingAI.Application.Efatura;

/// <summary>Uma linha lida de um CSV de compras do e-Fatura.</summary>
/// <param name="LineNumber">Linha no ficheiro (1 = cabeçalho).</param>
/// <param name="SupplierTaxId">NIF do emitente.</param>
/// <param name="SupplierName">Nome do emitente (sem o NIF à cabeça).</param>
/// <param name="InvoiceNo">Número da fatura.</param>
/// <param name="Atcud">ATCUD, quando o CSV o traz junto ao número.</param>
/// <param name="InvoiceDate">Data de emissão; nula se ilegível.</param>
/// <param name="InvoiceType">Tipo de documento (FT, FS, FR, ND, NC…).</param>
/// <param name="Status">Situação (Registada, …).</param>
/// <param name="Sector">Setor.</param>
/// <param name="TaxBase">Valor tributável.</param>
/// <param name="TaxAmount">Valor do IVA.</param>
/// <param name="GrossTotal">Valor total.</param>
/// <param name="Error">Motivo em pt-PT quando a linha não é utilizável; nulo se está bem.</param>
public sealed record EFaturaCsvRow(
    int LineNumber,
    string SupplierTaxId,
    string SupplierName,
    string InvoiceNo,
    string? Atcud,
    DateOnly? InvoiceDate,
    string InvoiceType,
    string? Status,
    string? Sector,
    decimal TaxBase,
    decimal TaxAmount,
    decimal GrossTotal,
    string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>Resultado da leitura de um CSV de compras do e-Fatura.</summary>
/// <param name="Rows">Linhas lidas, válidas e inválidas.</param>
/// <param name="Warnings">Avisos ao nível do ficheiro (p. ex. 300 registos).</param>
public sealed record EFaturaCsvResult(IReadOnlyList<EFaturaCsvRow> Rows, IReadOnlyList<string> Warnings)
{
    public int ValidCount => Rows.Count(row => row.IsValid);

    public int InvalidCount => Rows.Count(row => !row.IsValid);
}

/// <summary>Lê um CSV de compras do e-Fatura (adquirente). Não escreve nada.</summary>
public interface IEFaturaCsvReader
{
    Task<EFaturaCsvResult> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <summary>Lançada quando o conteúdo não é um CSV do e-Fatura legível.</summary>
public sealed class EFaturaCsvFormatException(string message) : Exception(message);
