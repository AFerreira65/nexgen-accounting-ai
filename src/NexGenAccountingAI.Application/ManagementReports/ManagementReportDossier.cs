namespace NexGenAccountingAI.Application.ManagementReports;

/// <summary>
/// Uma conta de movimento no período e no período homólogo. O montante é o
/// <em>movimento líquido</em> do período (débito − crédito) — para as classes 6
/// e 7 é o valor que entra na demonstração de resultados; para as contas de
/// balanço é a atividade do período.
/// </summary>
public sealed record DossierAccountDelta(
    string AccountCode,
    string AccountName,
    string Class,
    decimal Current,
    decimal Comparative,
    decimal Delta);

/// <summary>
/// Uma rubrica agregada (resultados ou posição financeira) no período e no
/// homólogo.
/// </summary>
public sealed record DossierAmount(string Label, decimal Current, decimal Comparative)
{
    public decimal Delta => decimal.Round(Current - Comparative, 2);
}

/// <summary>
/// Um rácio de gestão. <see cref="Value"/> é <c>null</c> quando o denominador é
/// nulo (o motor não inventa). <see cref="Formula"/> e <see cref="Accounts"/>
/// dizem exatamente de onde saiu, para a resposta poder ligar à origem.
/// </summary>
public sealed record DossierRatio(
    string Key,
    string Label,
    decimal? Value,
    string Unit,
    string Formula,
    IReadOnlyList<string> Accounts);

/// <summary>Um dos maiores movimentos do período, por valor absoluto.</summary>
public sealed record DossierMovement(
    string Reference,
    DateOnly Date,
    string Description,
    decimal Amount,
    IReadOnlyList<string> AccountCodes);

/// <summary>
/// Resumo do cruzamento compras e-Fatura × contabilidade para o período
/// (F2-03). <see cref="Available"/> é <c>false</c> quando não há dados de
/// e-Fatura para o período.
/// </summary>
public sealed record DossierCrossCheck(
    bool Available,
    decimal EfaturaTotal,
    decimal AccountingTotal,
    decimal TotalDelta,
    int UnbookedInvoiceCount,
    int UnmatchedEntryCount,
    bool PeriodBalanced);

/// <summary>
/// O retrato estruturado e limitado de uma empresa num período, montado de forma
/// determinística a partir dos serviços de leitura. É o único input de dados que
/// chega ao modelo (ADR-0006) e é guardado com o relatório para o tornar
/// reproduzível.
/// </summary>
public sealed record ManagementReportDossier(
    string CompanyName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly? ComparativeFrom,
    DateOnly? ComparativeTo,
    bool HasComparative,
    IReadOnlyList<DossierAccountDelta> TrialBalanceDelta,
    IReadOnlyList<DossierAmount> IncomeSummary,
    IReadOnlyList<DossierAmount> PositionSummary,
    IReadOnlyList<DossierRatio> Ratios,
    DossierCrossCheck CrossCheck,
    IReadOnlyList<DossierMovement> TopMovements,
    IReadOnlyList<string> Limits)
{
    /// <summary>Máximo de contas listadas no <see cref="TrialBalanceDelta"/>.</summary>
    public const int MaxAccountRows = 120;

    /// <summary>Máximo de movimentos listados no <see cref="TopMovements"/>.</summary>
    public const int MaxMovements = 25;
}
