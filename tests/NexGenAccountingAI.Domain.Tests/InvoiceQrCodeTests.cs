using NexGenAccountingAI.Domain.Archive;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class InvoiceQrCodeTests
{
    [TestMethod]
    public void ReadsTheInvoiceExampleOfTheTechnicalSpecification()
    {
        // O exemplo 1 das especificações técnicas da AT (fatura com os três espaços fiscais).
        const string text = "A:123456789*B:999999990*C:PT*D:FT*E:N*F:20191231*G:FT AB2019/0035*H:CSDF7T5H-0035*I1:PT*I2:12000.00*I3:15000.00*I4:900.00*I5:50000.00*I6:6500.00*I7:80000.00*I8:18400.00*J1:PT-AC*J2:10000.00*J3:25000.56*J4:1000.02*J5:75000.00*J6:6750.00*J7:100000.00*J8:18000.00*K1:PT-MA*K2:5000.00*K3:12500.00*K4:625.00*K5:25000.00*K6:3000.00*K7:40000.00*K8:8800.00*L:100.00*M:25.00*N:64000.02*O:513600.58*P:100.00*Q:kLp0*R:9999*S:TB;PT00000000000000000000000;513500.58";

        var qr = InvoiceQrCode.Parse(text);

        Assert.AreEqual("123456789", qr.IssuerTaxId);
        Assert.AreEqual("999999990", qr.BuyerTaxId);
        Assert.AreEqual("FT", qr.DocumentType);
        Assert.AreEqual(new DateOnly(2019, 12, 31), qr.DocumentDate);
        Assert.AreEqual("FT AB2019/0035", qr.DocumentNo);
        Assert.AreEqual("CSDF7T5H-0035", qr.Atcud);
        Assert.AreEqual(3, qr.Regions.Count);
        Assert.AreEqual(18400m, qr.Regions[0].NormalVat);
        Assert.AreEqual("PT-MA", qr.Regions[2].Region);
        Assert.AreEqual(64000.02m, qr.TaxTotal);
        Assert.AreEqual(513600.58m, qr.GrossTotal);
        Assert.AreEqual(449600.56m, qr.NetTotal);
        Assert.AreEqual(25m, qr.StampDuty);
        Assert.AreEqual("TB;PT00000000000000000000000;513500.58", qr.OtherInformation);
        Assert.AreEqual(0, qr.Warnings.Count);
    }

    [TestMethod]
    public void ReadsASimplifiedInvoiceAndReportsMissingMandatoryFields()
    {
        var qr = InvoiceQrCode.Parse("A:123456789*B:999999990*C:PT*D:FS*E:N*F:20190812*G:FS CDVF/12345*H:CDF7T5HD-12345*I1:PT*I7:0.65*I8:0.15*N:0.15*O:0.80*Q:YhGV*R:9999*S:NU;0.80");

        Assert.AreEqual(0.65m, qr.Regions.Single().NormalBase);
        Assert.AreEqual(0.15m, qr.Vat);
        Assert.AreEqual(0, qr.Warnings.Count);

        var incomplete = InvoiceQrCode.Parse("A:123456789*G:FT 1/2*O:12.30*F:2026-03-01");
        CollectionAssert.Contains(incomplete.Warnings.ToList(), "Falta o campo obrigatório H.");
        Assert.IsNull(incomplete.DocumentDate);
        Assert.IsTrue(incomplete.Warnings.Any(warning => warning.Contains("AAAAMMDD")));
    }

    [TestMethod]
    public void RejectsTextThatIsNotAnInvoiceQrCode()
    {
        Assert.ThrowsExactly<FormatException>(() => InvoiceQrCode.Parse(""));
        Assert.ThrowsExactly<FormatException>(() => InvoiceQrCode.Parse("https://exemplo.pt"));
        Assert.ThrowsExactly<FormatException>(() => InvoiceQrCode.Parse("A:123456789*G:FT 1/1"));
    }
}
