namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Uma regra de negócio sobre o <see cref="SaftValidationModel"/> — o que o XSD
/// não exprime. Corre sempre (mesmo com o esquema a falhar) e devolve zero ou
/// mais ocorrências.
/// </summary>
public interface ISaftAnalyticalRule
{
    IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model);
}

/// <summary>Corre todas as regras analíticas registadas sobre um modelo.</summary>
public interface ISaftAnalyticalValidator
{
    IReadOnlyList<SaftValidationIssue> Validate(SaftValidationModel model);
}
