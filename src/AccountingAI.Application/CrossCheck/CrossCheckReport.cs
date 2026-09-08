namespace AccountingAI.Application.CrossCheck;

/// <summary>
/// Uma linha mensal da perspetiva das compras: o que o e-Fatura conhece nesse
/// mês face ao que a contabilidade tem lançado em contas de gasto (classe 6) e
/// IVA dedutível (<c>2432*</c>).
/// </summary>
public sealed record CrossCheckPurchaseMonth(
    int Year,
    int Month,
    decimal EfaturaBase,
    decimal EfaturaVat,
    int EfaturaCount,
    decimal AccountingBase,
    decimal AccountingVat,
    int AccountingEntryCount)
{
    public decimal EfaturaTotal => decimal.Round(EfaturaBase + EfaturaVat, 2);

    public decimal AccountingTotal => decimal.Round(AccountingBase + AccountingVat, 2);

    public decimal BaseDelta => decimal.Round(AccountingBase - EfaturaBase, 2);

    public decimal VatDelta => decimal.Round(AccountingVat - EfaturaVat, 2);

    public decimal TotalDelta => decimal.Round(AccountingTotal - EfaturaTotal, 2);

    /// <summary>`true` quando a base e o IVA batem certo (a 2 casas) no mês.</summary>
    public bool Balanced => BaseDelta == 0m && VatDelta == 0m;
}

/// <summary>Uma fatura do e-Fatura sem lançamento de compra correspondente na contabilidade.</summary>
public sealed record CrossCheckUnbookedInvoice(
    Guid Id,
    string SupplierTaxId,
    string SupplierName,
    string InvoiceNo,
    string? Atcud,
    DateOnly InvoiceDate,
    decimal TaxBase,
    decimal TaxAmount,
    decimal GrossTotal);

/// <summary>Um lançamento de compra da contabilidade sem fatura do e-Fatura correspondente.</summary>
public sealed record CrossCheckUnmatchedEntry(
    string Reference,
    DateOnly EntryDate,
    string DocumentReference,
    string Description,
    decimal PurchaseBase,
    decimal DeductibleVat)
{
    public decimal Total => decimal.Round(PurchaseBase + DeductibleVat, 2);
}

/// <summary>
/// Perspetiva das compras do relatório de cruzamento: totais do período,
/// desagregação por mês, e as duas listas de divergências (faturas do e-Fatura
/// por lançar e lançamentos de compra sem fatura no e-Fatura).
/// </summary>
public sealed record CrossCheckPurchases(
    decimal EfaturaBase,
    decimal EfaturaVat,
    int EfaturaCount,
    decimal AccountingBase,
    decimal AccountingVat,
    int AccountingEntryCount,
    int MatchedCount,
    IReadOnlyList<CrossCheckPurchaseMonth> Months,
    IReadOnlyList<CrossCheckUnbookedInvoice> EfaturaWithoutAccounting,
    IReadOnlyList<CrossCheckUnmatchedEntry> AccountingWithoutEfatura)
{
    public decimal EfaturaTotal => decimal.Round(EfaturaBase + EfaturaVat, 2);

    public decimal AccountingTotal => decimal.Round(AccountingBase + AccountingVat, 2);

    public decimal BaseDelta => decimal.Round(AccountingBase - EfaturaBase, 2);

    public decimal VatDelta => decimal.Round(AccountingVat - EfaturaVat, 2);

    public decimal TotalDelta => decimal.Round(AccountingTotal - EfaturaTotal, 2);

    public bool Balanced => BaseDelta == 0m && VatDelta == 0m;

    /// <summary>Faturas do e-Fatura sem correspondência — o sinal para as Tarefas/painel.</summary>
    public int UnbookedCount => EfaturaWithoutAccounting.Count;
}

/// <summary>Uma linha mensal da perspetiva das vendas (rendimentos e IVA liquidado da contabilidade).</summary>
public sealed record CrossCheckSalesMonth(int Year, int Month, decimal Income, decimal OutputVat)
{
    public decimal Total => decimal.Round(Income + OutputVat, 2);
}

/// <summary>
/// Perspetiva das vendas: os rendimentos (classe 71/72, a crédito) e o IVA
/// liquidado (<c>2433*</c>) da contabilidade no período, por mês. Uma
/// reconciliação ao nível do documento fica dependente de importar o
/// <c>SalesInvoices</c> do SAF-T de faturação (F2-03E).
/// </summary>
public sealed record CrossCheckSales(
    decimal Income,
    decimal OutputVat,
    IReadOnlyList<CrossCheckSalesMonth> Months)
{
    public decimal Total => decimal.Round(Income + OutputVat, 2);
}

/// <summary>
/// Totais do período para a conferência «débito = crédito» — a mesma verificação
/// que a importação do SAF-T de contabilidade faz (F1-54), agora visível como
/// parte deste relatório.
/// </summary>
public sealed record CrossCheckTotals(decimal PeriodDebit, decimal PeriodCredit)
{
    public bool Balanced => decimal.Round(PeriodDebit - PeriodCredit, 2) == 0m;
}

/// <summary>
/// Relatório de cruzamento de uma empresa num intervalo de meses do exercício:
/// as compras do e-Fatura face aos lançamentos de compra da contabilidade, as
/// vendas da contabilidade por mês, e os totais do período. É um apoio de
/// conferência — não altera lançamentos nem substitui o validador oficial.
/// </summary>
public sealed record CrossCheckReport(
    DateOnly From,
    DateOnly To,
    CrossCheckPurchases Purchases,
    CrossCheckSales Sales,
    CrossCheckTotals Totals);
