using NexGenAccountingAI.Application.Fiscal;

namespace NexGenAccountingAI.IntegrationTests;

[TestClass]
public sealed class ObligationCatalogTests
{
    private static DateOnly Due(string code, int year, int month) =>
        ObligationCatalog.DueDate(ObligationCatalog.Find(code), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    [TestMethod]
    public void ComputesTheLegalDueDates()
    {
        Assert.AreEqual(new DateOnly(2026, 5, 20), Due(ObligationCatalog.VatReturnMonthly, 2026, 3), "Março: dia 20 do 2.º mês seguinte.");
        Assert.AreEqual(new DateOnly(2026, 5, 25), Due(ObligationCatalog.VatPaymentMonthly, 2026, 3));
        Assert.AreEqual(new DateOnly(2026, 8, 20), Due(ObligationCatalog.VatReturnQuarterly, 2026, 6), "2.º trimestre: 20 de agosto.");
        Assert.AreEqual(new DateOnly(2027, 2, 20), Due(ObligationCatalog.VatReturnQuarterly, 2026, 12), "4.º trimestre: 20 de fevereiro.");
        Assert.AreEqual(new DateOnly(2026, 4, 20), Due(ObligationCatalog.RecapitulativeMonthly, 2026, 3));
        Assert.AreEqual(new DateOnly(2026, 4, 10), Due(ObligationCatalog.MonthlyPayroll, 2026, 3));
        Assert.AreEqual(new DateOnly(2026, 4, 20), Due(ObligationCatalog.Withholdings, 2026, 3));
        Assert.AreEqual(new DateOnly(2027, 5, 31), Due(ObligationCatalog.Modelo22, 2026, 12), "Último dia de maio do ano seguinte.");
        Assert.AreEqual(new DateOnly(2027, 7, 15), Due(ObligationCatalog.Ies, 2026, 12));
        Assert.AreEqual(new DateOnly(2027, 2, 28), Due(ObligationCatalog.Modelo10, 2026, 12), "Final de fevereiro.");
        Assert.AreEqual(new DateOnly(2028, 2, 29), Due(ObligationCatalog.Modelo10, 2027, 12), "Ano bissexto.");
        Assert.AreEqual(new DateOnly(2026, 7, 31), Due(ObligationCatalog.AdvancePayment1, 2026, 12), "Os pagamentos por conta são do próprio ano.");
        Assert.AreEqual(new DateOnly(2026, 9, 30), Due(ObligationCatalog.AdvancePayment2, 2026, 12));
        Assert.AreEqual(new DateOnly(2026, 12, 15), Due(ObligationCatalog.AdvancePayment3, 2026, 12));
        Assert.AreEqual(new DateOnly(2026, 4, 20), Due(ObligationCatalog.StampDuty, 2026, 3));
        Assert.AreEqual(new DateOnly(2026, 4, 5), Due(ObligationCatalog.InvoiceCommunication, 2026, 3));
        Assert.AreEqual(new DateOnly(2027, 7, 15), Due(ObligationCatalog.FiscalDossier, 2026, 12), "O dossier fiscal acompanha a IES.");
    }

    [TestMethod]
    public void ListsThePeriodsDueInAWindow()
    {
        var may = ObligationCatalog.Between(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        var vat = may.Single(item => item.Definition.Code == ObligationCatalog.VatReturnMonthly);
        Assert.AreEqual("2026-03", vat.PeriodKey);
        Assert.AreEqual("Março de 2026", vat.PeriodLabel);
        var quarterly = may.Single(item => item.Definition.Code == ObligationCatalog.VatReturnQuarterly);
        Assert.AreEqual("2026-T1", quarterly.PeriodKey);
        Assert.AreEqual("2025", may.Single(item => item.Definition.Code == ObligationCatalog.Modelo22).PeriodKey);
        Assert.IsTrue(may.All(item => item.DueDate.Month == 5));
        CollectionAssert.AreEqual(may.OrderBy(item => item.DueDate).ToList(), may.ToList(), "Por ordem de prazo.");
        Assert.ThrowsExactly<ArgumentException>(() => ObligationCatalog.Between(new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 1)));
        Assert.ThrowsExactly<ArgumentException>(() => ObligationCatalog.Find("NAO-HA"));
        Assert.IsTrue(ObligationCatalog.All.All(item => item.Source.Contains("artigo", StringComparison.Ordinal)), "Cada obrigação tem a fonte legal.");
    }
}
