namespace AccountingAI.Application.Vat;

/// <summary>
/// Uma conta 24.3 que se moveu no período do apuramento, com o papel que
/// desempenha. <see cref="Role"/> ∈
/// <c>liquidado | dedutivel | regularizacoes | reporte | outras</c>.
/// </summary>
public sealed record VatReturnAccountLine(
    string AccountCode,
    string AccountName,
    string Role,
    decimal Debit,
    decimal Credit,
    decimal Net);

/// <summary>
/// O apuramento contabilístico de IVA de uma empresa num período: o movimento
/// das contas 24.3, com a aritmética do fecho de IVA (ADR-0007). Não é a
/// declaração periódica oficial da AT.
/// </summary>
/// <param name="OutputVat">IVA liquidado — Σ (crédito − débito) nas contas <c>2433*</c>.</param>
/// <param name="InputVat">IVA dedutível — Σ (débito − crédito) nas contas <c>2432*</c>.</param>
/// <param name="RegularisationsForState">Regularizações a favor do Estado — Σ crédito nas <c>2434*</c>.</param>
/// <param name="RegularisationsForTaxpayer">Regularizações a favor do sujeito passivo — Σ débito nas <c>2434*</c>.</param>
/// <param name="PriorPeriodCredit">Excesso a reportar do período anterior (saldo devedor de <c>2437*</c> antes do início do período).</param>
/// <param name="PeriodResult">(liquidado + reg. a favor do Estado) − (dedutível + reg. a favor do sujeito passivo).</param>
/// <param name="NetPayable">Valor a entregar ao Estado, quando o resultado menos o excesso anterior é positivo; senão 0.</param>
/// <param name="CreditToCarryForward">Excesso a reportar para o período seguinte, quando é negativo; senão 0.</param>
/// <param name="PriorCreditAssumed">`true` quando o excesso anterior foi assumido do razão (não indicado pelo utilizador).</param>
public sealed record VatReturn(
    DateOnly From,
    DateOnly To,
    decimal OutputVat,
    decimal InputVat,
    decimal RegularisationsForState,
    decimal RegularisationsForTaxpayer,
    decimal PriorPeriodCredit,
    decimal PeriodResult,
    decimal NetPayable,
    decimal CreditToCarryForward,
    bool PriorCreditAssumed,
    IReadOnlyList<VatReturnAccountLine> Lines);
