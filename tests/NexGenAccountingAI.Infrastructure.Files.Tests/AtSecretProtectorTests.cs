using NexGenAccountingAI.Infrastructure.At;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class AtSecretProtectorTests
{
    private string keyRing = string.Empty;

    [TestInitialize]
    public void SetUp() =>
        keyRing = Path.Combine(Path.GetTempPath(), $"at-keys-{Guid.NewGuid():N}");

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(keyRing))
        {
            Directory.Delete(keyRing, recursive: true);
        }
    }

    [TestMethod]
    public void ProtectsAndUnprotectsARoundTripWithTheEnc1Prefix()
    {
        var protector = AtSecretProtector.Create(keyRing);

        var protectedValue = protector.Protect("senha-do-certificado");

        StringAssert.StartsWith(protectedValue, "enc:v1:");
        Assert.IsTrue(protector.IsProtected(protectedValue));
        Assert.AreEqual("senha-do-certificado", protector.Unprotect(protectedValue));
    }

    [TestMethod]
    public void LeavesPlainTextAndUndecryptableValuesUnchanged()
    {
        var protector = AtSecretProtector.Create(keyRing);

        Assert.AreEqual("texto-simples", protector.Unprotect("texto-simples"));
        Assert.IsFalse(protector.IsProtected("texto-simples"));
        Assert.AreEqual(string.Empty, protector.Unprotect(null));
        Assert.AreEqual("enc:v1:lixo-que-nao-decifra", protector.Unprotect("enc:v1:lixo-que-nao-decifra"));
    }

    [TestMethod]
    public void ADifferentKeyRingCannotDecryptTheValue()
    {
        var protectedValue = AtSecretProtector.Create(keyRing).Protect("segredo");

        var other = Path.Combine(Path.GetTempPath(), $"at-keys-{Guid.NewGuid():N}");
        try
        {
            var status = AtSecretProtector.Create(other).Unprotect(protectedValue);
            Assert.AreEqual(protectedValue, status, "Outro chaveiro devolve o valor inalterado, não decifrado.");
        }
        finally
        {
            if (Directory.Exists(other))
            {
                Directory.Delete(other, recursive: true);
            }
        }
    }
}
