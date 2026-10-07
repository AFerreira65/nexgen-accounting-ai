using NexGenAccountingAI.Domain.Organizations;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class AtGeneralSettingsTests
{
    private static readonly byte[] SomeBytes = [1, 2, 3, 4];

    [TestMethod]
    public void TheCertificateIsStoredWithItsPasswordAndOnlyTheFileNameIsKept()
    {
        var settings = AtGeneralSettings.Create();

        settings.SetCertificate(@"C:\Users\x\Downloads\TesteWebservices.pfx", SomeBytes, "enc:v2:cifrada");

        Assert.AreEqual("TesteWebservices.pfx", settings.CertificateFileName);
        CollectionAssert.AreEqual(SomeBytes, settings.CertificateData);
        Assert.AreEqual("enc:v2:cifrada", settings.EncryptedCertificatePassword);
        Assert.AreEqual(AtGeneralSettings.SingletonKey, settings.Key);
    }

    [TestMethod]
    public void ThePublicKeyIsStoredIndependentlyOfTheCertificate()
    {
        var settings = AtGeneralSettings.Create();

        settings.SetPublicKey("ChaveCifraPublicaAT2027.CER", SomeBytes);

        Assert.AreEqual("ChaveCifraPublicaAT2027.CER", settings.PublicKeyFileName);
        CollectionAssert.AreEqual(SomeBytes, settings.PublicKeyData);
        Assert.IsNull(settings.CertificateData);

        settings.ClearPublicKey();
        Assert.IsNull(settings.PublicKeyFileName);
        Assert.IsNull(settings.PublicKeyData);
    }

    [TestMethod]
    public void FilesWithTheWrongTypeOrSizeOrNameAreRefused()
    {
        var settings = AtGeneralSettings.Create();

        Assert.ThrowsExactly<ArgumentException>(() => settings.SetCertificate("certificado.cer", SomeBytes, "enc:v2:x"));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetCertificate("certificado.pfx", [], "enc:v2:x"));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetCertificate("certificado.pfx", new byte[AtGeneralSettings.FileMaxBytes + 1], "enc:v2:x"));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetCertificate(null, SomeBytes, "enc:v2:x"));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetCertificate("certificado.pfx", SomeBytes, " "));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetPublicKey("chave.pfx", SomeBytes));
        Assert.ThrowsExactly<ArgumentException>(() => settings.SetPublicKey(new string('a', 300) + ".cer", SomeBytes));
        Assert.IsNull(settings.CertificateData, "Os pedidos recusados não alteram nada.");
        Assert.IsNull(settings.PublicKeyData);
    }

    [TestMethod]
    public void ThePasswordCannotBeChangedBeforeThereIsACertificateAndGoesWithItWhenRemoved()
    {
        var settings = AtGeneralSettings.Create();

        Assert.ThrowsExactly<InvalidOperationException>(() => settings.SetEncryptedPassword("enc:v2:x"));

        settings.SetCertificate("a.p12", SomeBytes, "enc:v2:primeira");
        settings.SetEncryptedPassword("enc:v2:segunda");
        Assert.AreEqual("enc:v2:segunda", settings.EncryptedCertificatePassword);

        settings.ClearCertificate();
        Assert.IsNull(settings.CertificateFileName);
        Assert.IsNull(settings.CertificateData);
        Assert.IsNull(settings.EncryptedCertificatePassword);
    }

    [TestMethod]
    public void AUserIsOnlyTheProducerWhenMarkedAndCanBeUnmarked()
    {
        var user = new UserAccount(Guid.NewGuid(), "Produtor", "produtor@test.invalid");
        Assert.IsFalse(user.IsProducer);

        user.MarkAsProducer();
        Assert.IsTrue(user.IsProducer);

        user.ClearProducer();
        Assert.IsFalse(user.IsProducer);
    }
}
