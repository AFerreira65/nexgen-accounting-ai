using AccountingAI.Core.Configuration;

namespace AccountingAI.Core.Phc;

public sealed record PhcAggregatedVatLine(
    decimal VatRate,
    decimal NetAmount,
    decimal VatAmount,
    string TaxExemptionCode,
    string TaxExemptionReason)
{
    public decimal GrossAmount => NetAmount + VatAmount;

    public decimal TaxableBase => GrossAmount - VatAmount;
}

public sealed record PhcAggregatedInvoiceDraft(
    string TaxRegistrationNumber,
    DateOnly DocumentDate,
    int DocumentCount,
    int CancelledDocumentCount,
    IReadOnlyList<PhcAggregatedVatLine> Lines,
    string ImportReference,
    string CurrencyCode = "")
{
    public decimal NetTotal => Lines.Sum(line => line.TaxableBase);

    public decimal VatTotal => Lines.Sum(line => line.VatAmount);

    public decimal GrossTotal => Lines.Sum(line => line.GrossAmount);

    public string GroupLabel
    {
        get
        {
            var exemptions = Lines
                .Where(line => line.TaxExemptionCode.Length > 0 || line.TaxExemptionReason.Length > 0)
                .Select(line => (line.TaxExemptionCode, line.TaxExemptionReason))
                .Distinct()
                .ToList();
            if (exemptions.Count == 0)
            {
                return "Vendas tributadas";
            }

            if (exemptions.Count == 1)
            {
                var exemption = exemptions[0];
                return $"{exemption.TaxExemptionCode} — {exemption.TaxExemptionReason}".Trim(' ', '—');
            }

            return "Vendas agregadas com vários motivos de isenção";
        }
    }
}

public sealed record PhcAggregatedTargetLine(
    decimal SaftVatRate,
    decimal NetAmount,
    decimal VatAmount,
    string TaxExemptionCode,
    string TaxExemptionReason,
    string ArticleReference,
    string ArticleDesignation,
    string Unit,
    int Cpoc,
    int VatTable,
    decimal PhcVatRate,
    bool IsService,
    bool MovesStock)
{
    public decimal GrossAmount => NetAmount + VatAmount;

    public decimal TaxableBase => GrossAmount - VatAmount;
}

public sealed record PhcAggregatedInvoiceTarget(
    PhcCompanyConfiguration Company,
    PhcAggregatedInvoiceDraft Draft,
    long CustomerNumber,
    int CustomerEstablishment,
    string CustomerName,
    string CustomerTaxId,
    string CustomerAddress,
    string CustomerLocation,
    string CustomerPostalCode,
    string CustomerPhone,
    long DocumentNumber,
    string DocumentName,
    int InternalDocumentType,
    int Warehouse,
    string SaftDocumentType,
    IReadOnlyList<PhcAggregatedTargetLine> Lines);

public sealed record PhcAggregatedInvoiceResult(
    string FtStamp,
    long SequentialNumber,
    int LinesInserted,
    int TaxTotalsInserted,
    decimal GrossTotal);
