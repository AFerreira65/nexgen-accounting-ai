using NexGenAccountingAI.Application.At;

namespace NexGenAccountingAI.Application.Efatura;

/// <summary>
/// Uma fatura de compra tal como o serviço de consulta de faturas da AT (<c>fatshare</c>) a
/// devolve, como adquirente. O serviço não traz o nome do emitente.
/// </summary>
/// <param name="NetTotal">O total sem impostos (<c>NetTotal</c>).</param>
/// <param name="TaxPayable">O imposto a pagar (<c>TaxPayable</c>).</param>
public sealed record EFaturaAtInvoice(
    string SupplierTaxId,
    string InvoiceNo,
    string? Atcud,
    DateOnly InvoiceDate,
    string InvoiceType,
    string? Sector,
    decimal NetTotal,
    decimal TaxPayable,
    decimal GrossTotal);

/// <summary>O resultado de consultar as compras de um período na AT.</summary>
/// <param name="Reached">Se a AT chegou a responder.</param>
/// <param name="Code">O <c>EstadoOperacao</c> da AT; 200 é sucesso.</param>
/// <param name="Message">A mensagem da AT, ou a explicação da falha (inclui o mês em que falhou).</param>
/// <param name="Warnings">Documentos que a aplicação não conseguiu ler.</param>
public sealed record EFaturaAtFetchResult(
    bool Reached,
    int? Code,
    string Message,
    IReadOnlyList<EFaturaAtInvoice> Invoices,
    IReadOnlyList<string> Warnings,
    int? HttpStatus,
    long DurationMilliseconds)
{
    public bool Succeeded => Reached && Code == 200;
}

/// <summary>A consulta de faturas do e-Fatura (adquirente) no webservice da AT.</summary>
public interface IEFaturaAtGateway
{
    AtGatewayStatus Status();

    /// <summary>
    /// Pede à AT as faturas em que <paramref name="customerTaxId"/> é o adquirente, de
    /// <paramref name="from"/> a <paramref name="to"/> (a AT só responde a um mês de cada vez; a
    /// implementação parte o período). Nunca lança por falhas de comunicação.
    /// </summary>
    Task<EFaturaAtFetchResult> FetchPurchasesAsync(
        DateOnly from, DateOnly to, AtCredential credential, string customerTaxId, bool production, CancellationToken cancellationToken = default);
}

/// <summary>Uma compra pronta a gravar, venha de um CSV ou da AT.</summary>
public sealed record EFaturaPurchaseCandidate(
    string SupplierTaxId,
    string SupplierName,
    string InvoiceNo,
    string? Atcud,
    DateOnly InvoiceDate,
    string InvoiceType,
    string? Status,
    string? Sector,
    decimal TaxBase,
    decimal TaxAmount,
    decimal GrossTotal);
