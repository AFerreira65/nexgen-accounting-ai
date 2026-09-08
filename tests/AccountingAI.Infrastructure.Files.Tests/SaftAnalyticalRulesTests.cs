using AccountingAI.Application.Saft;
using AccountingAI.Application.Saft.Rules;
using AccountingAI.Domain.Saft;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class SaftAnalyticalRulesTests
{
    private static SaftValidationModel Clean() => new()
    {
        AuditFileVersion = "1.04_01",
        TaxRegistrationNumber = "500000000",
        TaxAccountingBasis = "I",
        FiscalYear = 2026,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
        ProductId = "Accounting AI/AccountingAI",
        ProductCompanyTaxId = "500000000",
        SoftwareCertificateNumber = "0",
        AccountIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "6221", "2211001" },
        JournalCount = 2,
        HasGeneralLedgerEntries = true,
        DeclaredNumberOfEntries = 3,
        DeclaredTotalDebit = 300m,
        DeclaredTotalCredit = 300m,
        TransactionCount = 3,
        LineDebitTotal = 300m,
        LineCreditTotal = 300m,
    };

    private static SaftAnalyticalValidator Validator() => new(
    [
        new NifCheckDigitRule(), new AccountingBasisRule(), new PeriodRangeRule(), new ProductIdentificationRule(),
        new EntryCountRule(), new FileTotalsRule(), new TransactionBalancedRule(), new AccountReferenceRule(),
        new EntryDatesInPeriodRule(), new AccountingHasJournalsRule()
    ]);

    private static bool HasError(IEnumerable<SaftValidationIssue> issues, string ruleId) =>
        issues.Any(issue => issue.RuleId == ruleId && issue.Severity == SaftValidationSeverity.Error);

    [TestMethod]
    public void ACleanModelRaisesNothing()
    {
        var issues = Validator().Validate(Clean());
        Assert.AreEqual(0, issues.Count, string.Join(" | ", issues.Select(i => $"{i.RuleId}:{i.Message}")));
    }

    [TestMethod]
    public void NifRuleFlagsABadCheckDigitAndAMissingNif()
    {
        Assert.IsTrue(HasError(new NifCheckDigitRule().Evaluate(Clean() with { TaxRegistrationNumber = "599999999" }), "NIF-CHECKDIGIT"));
        Assert.IsTrue(HasError(new NifCheckDigitRule().Evaluate(Clean() with { TaxRegistrationNumber = null }), "NIF-CHECKDIGIT"));
        // Um identificador não numérico é aviso, não erro.
        var foreign = new NifCheckDigitRule().Evaluate(Clean() with { TaxRegistrationNumber = "DEMO-PT-0001" }).Single();
        Assert.AreEqual(SaftValidationSeverity.Warning, foreign.Severity);
    }

    [TestMethod]
    public void BasisRuleFlagsAnUnknownValue()
    {
        Assert.IsTrue(HasError(new AccountingBasisRule().Evaluate(Clean() with { TaxAccountingBasis = "Z" }), "BASIS-KNOWN"));
        Assert.IsTrue(HasError(new AccountingBasisRule().Evaluate(Clean() with { TaxAccountingBasis = null }), "BASIS-KNOWN"));
        Assert.AreEqual(0, new AccountingBasisRule().Evaluate(Clean() with { TaxAccountingBasis = "c" }).Count());
    }

    [TestMethod]
    public void PeriodRuleFlagsAnInvertedRangeAndAMismatchedFiscalYear()
    {
        Assert.IsTrue(HasError(
            new PeriodRangeRule().Evaluate(Clean() with { StartDate = new DateOnly(2026, 12, 31), EndDate = new DateOnly(2026, 1, 1) }),
            "PERIOD-RANGE"));
        var mismatch = new PeriodRangeRule().Evaluate(Clean() with { FiscalYear = 2024 }).Single();
        Assert.AreEqual(SaftValidationSeverity.Warning, mismatch.Severity);
    }

    [TestMethod]
    public void EntryCountRuleFlagsAMismatch()
    {
        Assert.IsTrue(HasError(new EntryCountRule().Evaluate(Clean() with { DeclaredNumberOfEntries = 3, TransactionCount = 2 }), "ENTRY-COUNT"));
        Assert.AreEqual(0, new EntryCountRule().Evaluate(Clean() with { HasGeneralLedgerEntries = false }).Count());
    }

    [TestMethod]
    public void FileTotalsRuleFlagsSumsThatDoNotMatchOrTie()
    {
        Assert.IsTrue(HasError(new FileTotalsRule().Evaluate(Clean() with { LineDebitTotal = 290m }), "FILE-TOTALS"));
        Assert.IsTrue(HasError(new FileTotalsRule().Evaluate(Clean() with { DeclaredTotalCredit = 310m }), "FILE-TOTALS"));
    }

    [TestMethod]
    public void TransactionBalancedRuleFlagsEachUnbalancedTransaction()
    {
        var model = Clean() with
        {
            UnbalancedTransactions = [new SaftFlaggedTransaction("C 7", new DateOnly(2026, 3, 1), 100m, 90m)]
        };
        Assert.IsTrue(HasError(new TransactionBalancedRule().Evaluate(model), "TRANSACTION-BALANCED"));
    }

    [TestMethod]
    public void AccountReferenceRuleGroupsByAccount()
    {
        var model = Clean() with
        {
            UnresolvedAccountRefs =
            [
                new SaftUnresolvedAccountRef("C 1", "9999"),
                new SaftUnresolvedAccountRef("C 2", "9999")
            ]
        };
        var issues = new AccountReferenceRule().Evaluate(model).ToList();
        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0].Message, "9999");
    }

    [TestMethod]
    public void OutOfPeriodDatesAreAWarning()
    {
        var model = Clean() with
        {
            OutOfPeriodTransactions = [new SaftFlaggedTransaction("C 9", new DateOnly(2025, 12, 30), 10m, 10m)]
        };
        var issue = new EntryDatesInPeriodRule().Evaluate(model).Single();
        Assert.AreEqual(SaftValidationSeverity.Warning, issue.Severity);
        Assert.AreEqual("ENTRY-DATES-IN-PERIOD", issue.RuleId);
    }

    [TestMethod]
    public void AccountingBasisWithoutJournalsIsAWarning()
    {
        var issue = new AccountingHasJournalsRule().Evaluate(Clean() with { JournalCount = 0 }).Single();
        Assert.AreEqual(SaftValidationSeverity.Warning, issue.Severity);
        Assert.AreEqual(0, new AccountingHasJournalsRule().Evaluate(Clean() with { JournalCount = 0, TaxAccountingBasis = "F" }).Count());
    }

    [TestMethod]
    public void MissingProductIdentificationIsInformational()
    {
        var issue = new ProductIdentificationRule().Evaluate(Clean() with { SoftwareCertificateNumber = null }).Single();
        Assert.AreEqual(SaftValidationSeverity.Information, issue.Severity);
        StringAssert.Contains(issue.Message, "SoftwareCertificateNumber");
    }
}
