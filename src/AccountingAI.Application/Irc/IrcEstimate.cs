namespace AccountingAI.Application.Irc;

/// <summary>Uma correção fiscal introduzida pelo utilizador — livre, não validada contra a lei fiscal.</summary>
public sealed record IrcAdjustment(string Description, decimal Amount);

/// <summary>
/// A estimativa de IRC de um período: resultado líquido (classes 6/7) mais as
/// correções fiscais introduzidas pelo utilizador, vezes a taxa introduzida
/// pelo utilizador. Não é o Modelo 22 nem substitui o cálculo oficial — ver
/// <c>ADR-0010</c>.
/// </summary>
/// <param name="NetIncome">Resultado líquido do período (Σ classe 7 crédito−débito − Σ classe 6 débito−crédito).</param>
/// <param name="Adjustments">As correções fiscais tal como introduzidas.</param>
/// <param name="AdjustmentsTotal">A soma das correções.</param>
/// <param name="TaxableResult">Resultado fiscal = <see cref="NetIncome"/> + <see cref="AdjustmentsTotal"/>.</param>
/// <param name="TaxRate">A taxa introduzida pelo utilizador (fração, ex.: 0.21).</param>
/// <param name="EstimatedTax">Coleta estimada = <see cref="TaxableResult"/> × <see cref="TaxRate"/> quando positivo; senão 0.</param>
public sealed record IrcEstimate(
    DateOnly From,
    DateOnly To,
    decimal NetIncome,
    IReadOnlyList<IrcAdjustment> Adjustments,
    decimal AdjustmentsTotal,
    decimal TaxableResult,
    decimal TaxRate,
    decimal EstimatedTax);
