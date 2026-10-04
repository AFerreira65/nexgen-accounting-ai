using NexGenAccountingAI.Domain.Accounting;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class VatCodeTests
{
    [TestMethod]
    public void CreateNormalizesTheCodeAndRoundsThePercentage()
    {
        var code = VatCode.Create(
            Guid.NewGuid(), " v-nor ", "  Vendas — taxa normal ", VatRate.Normal, 23.004m,
            VatOperationNature.TransmissoesInternas, VatRegion.Continente);

        Assert.AreEqual("V-NOR", code.Code);
        Assert.AreEqual("Vendas — taxa normal", code.Name);
        Assert.AreEqual(23m, code.Percentage);
        Assert.IsTrue(code.IsActive);
    }

    [TestMethod]
    public void RejectsIncoherentCodes()
    {
        var companyId = Guid.NewGuid();
        VatCode Make(string code, string name, VatRate rate, decimal percentage, VatOperationNature nature) =>
            VatCode.Create(companyId, code, name, rate, percentage, nature, VatRegion.Continente);

        Assert.ThrowsExactly<ArgumentException>(() => Make("", "Nome", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V NOR", "Nome", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas));
        Assert.ThrowsExactly<ArgumentException>(() => Make("CODIGO-MUITO-LONGO", "Nome", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V-NOR", " ", VatRate.Normal, 23m, VatOperationNature.TransmissoesInternas));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V-NOR", "Nome", VatRate.Nenhuma, 23m, VatOperationNature.TransmissoesInternas));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V-NOR", "Nome", VatRate.Normal, 23m, VatOperationNature.Nenhuma));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V-NOR", "Nome", VatRate.Normal, 101m, VatOperationNature.TransmissoesInternas));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V-ISE", "Nome", VatRate.Isento, 6m, VatOperationNature.OperacoesIsentasSemDeducao));
        Assert.ThrowsExactly<ArgumentException>(() => Make("V-NOR", "Nome", VatRate.Normal, 0m, VatOperationNature.TransmissoesInternas));
    }
}
