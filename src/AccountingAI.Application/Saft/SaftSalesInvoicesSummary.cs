namespace AccountingAI.Application.Saft;

/// <summary>As faturas de vendas de um mês de calendário do SAF-T de faturação.</summary>
public sealed record SaftSalesInvoiceMonth(int Year, int Month, int Count, decimal Net, decimal Tax, decimal Gross)
{
    public decimal GrossOrSum => Gross == 0m ? decimal.Round(Net + Tax, 2) : Gross;
}

/// <summary>
/// Resumo do bloco <c>SourceDocuments/SalesInvoices</c> de um SAF-T (PT) de
/// faturação, obtido por leitura em streaming. Traz os totais declarados no
/// cabeçalho do bloco e a soma das faturas por mês (excluindo as anuladas), o
/// que dá uma fonte de vendas ao nível do documento para o cruzamento. Um
/// ficheiro sem o bloco devolve <c>HasSalesInvoices = false</c>.
/// </summary>
public sealed record SaftSalesInvoicesSummary(
    bool HasSalesInvoices,
    int? DeclaredNumberOfEntries,
    decimal? DeclaredTotalDebit,
    decimal? DeclaredTotalCredit,
    int InvoiceCount,
    int VoidedCount,
    decimal Net,
    decimal Tax,
    decimal Gross,
    DateOnly? FromDate,
    DateOnly? ToDate,
    IReadOnlyList<SaftSalesInvoiceMonth> Months);
