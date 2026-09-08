using AccountingAI.Domain.Saft;

namespace AccountingAI.Application.Saft.Rules;

/// <summary>`GeneralLedgerEntries/NumberOfEntries` = nº de transações lidas.</summary>
public sealed class EntryCountRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        if (!model.HasGeneralLedgerEntries || model.DeclaredNumberOfEntries is not { } declared)
        {
            yield break;
        }

        if (declared != model.TransactionCount)
        {
            yield return new SaftValidationIssue("ENTRY-COUNT", SaftValidationSeverity.Error, "GeneralLedgerEntries",
                $"NumberOfEntries declara {declared}; foram lidas {model.TransactionCount} transações.");
        }
    }
}

/// <summary>`TotalDebit`/`TotalCredit` do bloco = soma das linhas, e iguais entre si (ao cêntimo).</summary>
public sealed class FileTotalsRule : ISaftAnalyticalRule
{
    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        if (!model.HasGeneralLedgerEntries)
        {
            yield break;
        }

        if (model.DeclaredTotalDebit is { } declaredDebit && Cents(declaredDebit) != Cents(model.LineDebitTotal))
        {
            yield return new SaftValidationIssue("FILE-TOTALS", SaftValidationSeverity.Error, "GeneralLedgerEntries",
                $"TotalDebit declara {declaredDebit:0.00}; a soma das linhas de débito é {model.LineDebitTotal:0.00}.");
        }

        if (model.DeclaredTotalCredit is { } declaredCredit && Cents(declaredCredit) != Cents(model.LineCreditTotal))
        {
            yield return new SaftValidationIssue("FILE-TOTALS", SaftValidationSeverity.Error, "GeneralLedgerEntries",
                $"TotalCredit declara {declaredCredit:0.00}; a soma das linhas de crédito é {model.LineCreditTotal:0.00}.");
        }

        if (model.DeclaredTotalDebit is { } totalDebit && model.DeclaredTotalCredit is { } totalCredit && Cents(totalDebit) != Cents(totalCredit))
        {
            yield return new SaftValidationIssue("FILE-TOTALS", SaftValidationSeverity.Error, "GeneralLedgerEntries",
                $"TotalDebit ({totalDebit:0.00}) e TotalCredit ({totalCredit:0.00}) não são iguais.");
        }
    }
}

/// <summary>Cada transação com `Σ DebitAmount = Σ CreditAmount`.</summary>
public sealed class TransactionBalancedRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        foreach (var transaction in model.UnbalancedTransactions)
        {
            yield return new SaftValidationIssue("TRANSACTION-BALANCED", SaftValidationSeverity.Error, "GeneralLedgerEntries",
                $"A transação '{transaction.TransactionId}' não equilibra: débito {transaction.DebitTotal:0.00}, crédito {transaction.CreditTotal:0.00}.");
        }

        if (model.FlaggedListsTruncated && model.UnbalancedTransactions.Count > 0)
        {
            yield return new SaftValidationIssue("TRANSACTION-BALANCED", SaftValidationSeverity.Information, "GeneralLedgerEntries",
                "Há mais transações desequilibradas do que as listadas.");
        }
    }
}

/// <summary>Todo o `AccountID` referido nas linhas existe no plano do ficheiro.</summary>
public sealed class AccountReferenceRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        foreach (var group in model.UnresolvedAccountRefs.GroupBy(item => item.AccountId, StringComparer.OrdinalIgnoreCase))
        {
            var example = group.First().TransactionId;
            yield return new SaftValidationIssue("ACCOUNT-REF-RESOLVES", SaftValidationSeverity.Error, "GeneralLedgerEntries",
                $"A conta '{group.Key}' é usada em lançamentos (p. ex. '{example}') mas não existe em GeneralLedgerAccounts.");
        }
    }
}

/// <summary>Cada `TransactionDate` dentro do período do cabeçalho.</summary>
public sealed class EntryDatesInPeriodRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        foreach (var transaction in model.OutOfPeriodTransactions)
        {
            yield return new SaftValidationIssue("ENTRY-DATES-IN-PERIOD", SaftValidationSeverity.Warning, "GeneralLedgerEntries",
                $"A transação '{transaction.TransactionId}' tem data {transaction.Date:yyyy-MM-dd}, fora do período do cabeçalho.");
        }
    }
}

/// <summary>Base contabilística (C/I) com pelo menos um diário no ficheiro.</summary>
public sealed class AccountingHasJournalsRule : ISaftAnalyticalRule
{
    public IEnumerable<SaftValidationIssue> Evaluate(SaftValidationModel model)
    {
        var basis = model.TaxAccountingBasis?.Trim();
        var isAccounting = string.Equals(basis, "C", StringComparison.OrdinalIgnoreCase)
            || string.Equals(basis, "I", StringComparison.OrdinalIgnoreCase);

        if (isAccounting && model.JournalCount == 0)
        {
            yield return new SaftValidationIssue("ACCOUNTING-HAS-JOURNALS", SaftValidationSeverity.Warning, "GeneralLedgerEntries",
                "O cabeçalho indica contabilidade mas o ficheiro não tem diários com movimentos.");
        }
    }
}
