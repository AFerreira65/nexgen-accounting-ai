using NexGenAccountingAI.Domain.Organizations;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class AtGeneralSettingsTests
{
    [TestMethod]
    public void LocationsAreTrimmedAndBlankMeansNotSet()
    {
        var settings = AtGeneralSettings.Create();

        settings.SetLocations("  C:\\AT\\certificados\\  ", " TesteWebservices.pfx ", "C:\\AT\\chaves", "Chave.cer");
        Assert.AreEqual("C:\\AT\\certificados", settings.CertificateDirectory);
        Assert.AreEqual("TesteWebservices.pfx", settings.CertificateFileName);
        Assert.AreEqual("Chave.cer", settings.PublicKeyFileName);

        settings.SetLocations(" ", null, "", "  ");
        Assert.IsNull(settings.CertificateDirectory);
        Assert.IsNull(settings.CertificateFileName);
        Assert.IsNull(settings.PublicKeyDirectory);
        Assert.IsNull(settings.PublicKeyFileName);
    }

    [TestMethod]
    public void TheFolderAndTheNameComeTogether()
    {
        var settings = AtGeneralSettings.Create();

        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("C:\\AT", null, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations(null, "certificado.pfx", null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations(null, null, "C:\\AT", " "));
    }

    [TestMethod]
    public void TheFolderMustBeAbsoluteAndTheNameMustNotHoldFolders()
    {
        var settings = AtGeneralSettings.Create();

        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("certificados", "a.pfx", null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("..\\certificados", "a.pfx", null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("C:\\AT", "..\\fora\\a.pfx", null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("C:\\AT", "pasta/a.pfx", null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("C:\\AT", "C:a.pfx", null, null));
    }

    [TestMethod]
    public void OnlyCertificateAndPublicKeyFileTypesAreAccepted()
    {
        var settings = AtGeneralSettings.Create();

        settings.SetLocations("C:\\AT", "a.P12", "C:\\AT", "b.CRT");
        Assert.AreEqual("a.P12", settings.CertificateFileName);

        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("C:\\AT", "a.txt", null, null));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations("C:\\AT", "a.pfx", "C:\\AT", "b.pfx"));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetLocations(new string('C', 261), "a.pfx", null, null));
    }

    [TestMethod]
    public void ThePasswordIsKeptOnlyAsGiven()
    {
        var settings = AtGeneralSettings.Create();

        settings.SetEncryptedPassword("enc:v1:abc");
        Assert.AreEqual("enc:v1:abc", settings.EncryptedCertificatePassword);

        settings.SetEncryptedPassword(null);
        Assert.IsNull(settings.EncryptedCertificatePassword);
    }
}
