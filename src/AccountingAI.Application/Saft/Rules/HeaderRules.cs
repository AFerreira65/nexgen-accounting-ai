using AccountingAI.Domain.Saft;

namespace AccountingAI.Application.Saft.Rules;

/// <summary>`TaxRegistrationNumber` com nove dígitos e dígito de controlo válido.</summary>
public sealed class NifCheckDigitRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        var nif = model.TaxRegistrationNumber;
        if (string.IsNullOrWhiteSpace(nif))
        {
            yield return new SaftValidationIssue("NIF-CHECKDIGIT", SaftValidationSeverity.Error, "Header",
                "O cabeçalho não tem TaxRegistrationNumber.");
            yield break;
        }

        if (!PortugueseTaxNumber.IsAllDigits(nif))
        {
            yield return new SaftValidationIssue("NIF-CHECKDIGIT", SaftValidationSeverity.Warning, "Header",
                $"O NIF do ficheiro ('{nif}') não é numérico — não foi possível verificar o dígito de controlo.");
            yield break;
        }

        if (!PortugueseTaxNumber.IsValid(nif))
        {
            yield return new SaftValidationIssue("NIF-CHECKDIGIT", SaftValidationSeverity.Error, "Header",
                $"O NIF do ficheiro ('{nif}') não tem nove dígitos ou o dígito de controlo não confere.");
        }
    }
}

/// <summary>`TaxAccountingBasis` é um dos valores previstos.</summary>
public sealed class AccountingBasisRule : ISaftAnalyticalRule
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "C", "E", "F", "I", "P", "R", "S", "T"
    };

    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        var basis = model.TaxAccountingBasis;
        if (string.IsNullOrWhiteSpace(basis))
        {
            yield return new SaftValidationIssue("BASIS-KNOWN", SaftValidationSeverity.Error, "Header",
                "O cabeçalho não tem TaxAccountingBasis.");
        }
        else if (!Known.Contains(basis.Trim()))
        {
            yield return new SaftValidationIssue("BASIS-KNOWN", SaftValidationSeverity.Error, "Header",
                $"TaxAccountingBasis ('{basis}') não é um valor previsto (C, E, F, I, P, R, S, T).");
        }
    }
}

/// <summary>`StartDate ≤ EndDate` e `FiscalYear` coerente com o período.</summary>
public sealed class PeriodRangeRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        if (model.StartDate is not { } start || model.EndDate is not { } end)
        {
            yield return new SaftValidationIssue("PERIOD-RANGE", SaftValidationSeverity.Error, "Header",
                "O cabeçalho não tem StartDate/EndDate legíveis.");
            yield break;
        }

        if (start > end)
        {
            yield return new SaftValidationIssue("PERIOD-RANGE", SaftValidationSeverity.Error, "Header",
                $"A data inicial ({start:yyyy-MM-dd}) é posterior à data final ({end:yyyy-MM-dd}).");
        }

        if (model.FiscalYear is { } year && year != start.Year && year != end.Year)
        {
            yield return new SaftValidationIssue("PERIOD-RANGE", SaftValidationSeverity.Warning, "Header",
                $"FiscalYear ({year}) não coincide com o ano do período ({start.Year}–{end.Year}).");
        }
    }
}

/// <summary>Identificação do produtor de software (informativo).</summary>
public sealed class ProductIdentificationRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(model.ProductId)) { missing.Add("ProductID"); }
        if (string.IsNullOrWhiteSpace(model.ProductCompanyTaxId)) { missing.Add("ProductCompanyTaxID"); }
        if (string.IsNullOrWhiteSpace(model.SoftwareCertificateNumber)) { missing.Add("SoftwareCertificateNumber"); }

        if (missing.Count > 0)
        {
            yield return new SaftValidationIssue("PRODUCT-COMPANY-TIN", SaftValidationSeverity.Information, "Header",
                $"O cabeçalho não traz {string.Join(", ", missing)}.");
        }
    }
}
