using NexGenAccountingAI.Application.Vat;
using NexGenAccountingAI.Domain.Accounting;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class VatDeclarationFieldCatalogTests
{
    [TestMethod]
    public void MapsTheBaseAndTaxFieldsPerRateForInternalOperations()
    {
        Assert.AreEqual(1, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.TransmissoesInternas, VatRate.Reduzida, VatRegion.Continente)?.Number);
        Assert.AreEqual(2, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.TransmissoesInternas, VatRate.Reduzida, VatRegion.Continente)?.Number);
        Assert.AreEqual(5, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.PrestacoesServicosInternas, VatRate.Intermedia, VatRegion.Continente)?.Number);
        Assert.AreEqual(6, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.PrestacoesServicosInternas, VatRate.Intermedia, VatRegion.Continente)?.Number);
        Assert.AreEqual(3, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.TransmissoesInternas, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(4, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.TransmissoesInternas, VatRate.Normal, VatRegion.Continente)?.Number);
    }

    [TestMethod]
    public void MapsTheDeductibleVatFieldsByCategoryAndRate()
    {
        Assert.AreEqual(20, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.IvaDedutivelImobilizado, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(21, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.IvaDedutivelExistencias, VatRate.Reduzida, VatRegion.Continente)?.Number);
        Assert.AreEqual(23, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.IvaDedutivelExistencias, VatRate.Intermedia, VatRegion.Continente)?.Number);
        Assert.AreEqual(22, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.IvaDedutivelExistencias, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(24, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.IvaDedutivelOutros, VatRate.Normal, VatRegion.Continente)?.Number);

        // A base de uma compra não tem campo próprio.
        Assert.IsNull(VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.IvaDedutivelOutros, VatRate.Normal, VatRegion.Continente));
    }

    [TestMethod]
    public void MapsExemptIntracommunityAndRegularisationFields()
    {
        Assert.AreEqual(8, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.ExportacoesEIsentasComDeducao, VatRate.Isento, VatRegion.Continente)?.Number);
        Assert.AreEqual(9, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.OperacoesIsentasSemDeducao, VatRate.Isento, VatRegion.Continente)?.Number);
        Assert.AreEqual(12, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.AquisicoesIntracomunitariasBens, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(13, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.AquisicoesIntracomunitariasBens, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(16, VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.AquisicoesIntracomunitariasServicos, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(17, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.AquisicoesIntracomunitariasServicos, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(40, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.RegularizacoesFavorSujeitoPassivo, VatRate.Normal, VatRegion.Continente)?.Number);
        Assert.AreEqual(41, VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.RegularizacoesFavorEstado, VatRate.Normal, VatRegion.Continente)?.Number);
    }

    [TestMethod]
    public void AnnotatesRowsOutsideTheCompanysOwnRegionWithTheAnexoRNote()
    {
        var continente = VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.TransmissoesInternas, VatRate.Normal, VatRegion.Continente);
        var acores = VatDeclarationFieldCatalog.ForRow("base", VatOperationNature.TransmissoesInternas, VatRate.Normal, VatRegion.Acores);

        Assert.IsNull(continente!.Note);
        Assert.AreEqual(3, acores!.Number, "O mesmo número de campo aplica-se no Anexo R.");
        Assert.IsNotNull(acores.Note);
        StringAssert.Contains(acores.Note, "Anexo R");
    }

    [TestMethod]
    public void ReturnsNullForCombinationsWithoutAnUnambiguousField()
    {
        Assert.IsNull(VatDeclarationFieldCatalog.ForRow("imposto", VatOperationNature.AutoliquidacaoOutras, VatRate.Normal, VatRegion.Continente));
        Assert.IsNull(VatDeclarationFieldCatalog.ForRow("outras", VatOperationNature.Nenhuma, VatRate.Nenhuma, VatRegion.Continente));
    }
}
