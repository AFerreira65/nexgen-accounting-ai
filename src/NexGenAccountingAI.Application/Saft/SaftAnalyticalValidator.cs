namespace NexGenAccountingAI.Application.Saft;

/// <summary>Corre as regras analíticas registadas e junta as ocorrências.</summary>
public sealed class SaftAnalyticalValidator(IEnumerable<ISaftAnalyticalRule> rules) : ISaftAnalyticalValidator
{
    private readonly IReadOnlyList<ISaftAnalyticalRule> _rules = rules.ToList();

    public IReadOnlyList<SaftValidationIssue> Validate(SaftValidationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _rules.SelectMany(rule => rule.Evaluate(model)).ToList();
    }
}
