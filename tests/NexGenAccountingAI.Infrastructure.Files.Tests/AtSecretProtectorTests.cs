using Microsoft.AspNetCore.DataProtection;
using NexGenAccountingAI.Infrastructure.At;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class AtSecretProtectorTests
{
    private readonly List<string> keyRings = [];

    private string NewKeyRing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"at-keys-{Guid.NewGuid():N}");
        keyRings.Add(path);
        return path;
    }

    [TestCleanup]
    public void TearDown()
    {
        foreach (var path in keyRings.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [TestMethod]
    public void ProtectsAndUnprotectsARoundTripWithThePortablePrefix()
    {
        var protector = AtSecretProtector.Create(NewKeyRing());

        var protectedValue = protector.Protect("senha-do-certificado");

        StringAssert.StartsWith(protectedValue, "enc:v2:");
        Assert.IsFalse(protectedValue.Contains("senha-do-certificado", StringComparison.Ordinal));
        Assert.IsTrue(protector.IsProtected(protectedValue));
        Assert.AreEqual("senha-do-certificado", protector.Unprotect(protectedValue));
        Assert.AreEqual("Pâ$$wörd com acentos e símbolos €", protector.Unprotect(protector.Protect("Pâ$$wörd com acentos e símbolos €")));
    }

    [TestMethod]
    public void EveryProtectionOfTheSameValueIsDifferent()
    {
        var protector = new AtSecretProtector();

        var first = protector.Protect("segredo");
        var second = protector.Protect("segredo");

        Assert.AreNotEqual(first, second, "Sal e nonce aleatórios em cada valor.");
        Assert.AreEqual("segredo", protector.Unprotect(first));
        Assert.AreEqual("segredo", protector.Unprotect(second));
    }

    [TestMethod]
    public void APortableValueIsDecryptedByAnyStationWithoutTheirKeyRing()
    {
        // Um posto cifra com o seu chaveiro; outro, com um chaveiro diferente (ou sem ele), decifra.
        var protectedValue = AtSecretProtector.Create(NewKeyRing()).Protect("segredo");

        Assert.AreEqual("segredo", AtSecretProtector.Create(NewKeyRing()).Unprotect(protectedValue));
        Assert.AreEqual("segredo", new AtSecretProtector().Unprotect(protectedValue));
    }

    [TestMethod]
    public void AValueFromTheOlderPerMachineFormatStillOpensOnThatMachineOnly()
    {
        var keyRing = NewKeyRing();
        var legacy = DataProtectionProvider.Create(new DirectoryInfo(keyRing)).CreateProtector("AccountingAI.At.Certificate.v1");
        var oldValue = "enc:v1:" + legacy.Protect("senha-antiga");

        Assert.IsTrue(AtSecretProtector.Create(keyRing).IsProtected(oldValue));
        Assert.AreEqual("senha-antiga", AtSecretProtector.Create(keyRing).Unprotect(oldValue));
        Assert.AreEqual(oldValue, AtSecretProtector.Create(NewKeyRing()).Unprotect(oldValue), "Noutra máquina o formato antigo não abre.");
        Assert.AreEqual(oldValue, new AtSecretProtector().Unprotect(oldValue));
    }

    [TestMethod]
    public void LeavesPlainTextAndUndecryptableOrTamperedValuesUnchanged()
    {
        var protector = new AtSecretProtector();

        Assert.AreEqual("texto-simples", protector.Unprotect("texto-simples"));
        Assert.IsFalse(protector.IsProtected("texto-simples"));
        Assert.AreEqual(string.Empty, protector.Unprotect(null));
        Assert.AreEqual("enc:v1:lixo-que-nao-decifra", protector.Unprotect("enc:v1:lixo-que-nao-decifra"));
        Assert.AreEqual("enc:v2:lixo", protector.Unprotect("enc:v2:lixo"));
        Assert.AreEqual("enc:v2:AAAA", protector.Unprotect("enc:v2:AAAA"));

        // Um valor alterado depois de cifrado falha a autenticação e não devolve texto nenhum.
        var value = protector.Protect("segredo");
        var bytes = Convert.FromBase64String(value["enc:v2:".Length..]);
        bytes[^1] ^= 0x01;
        var tampered = "enc:v2:" + Convert.ToBase64String(bytes);
        Assert.AreEqual(tampered, protector.Unprotect(tampered));
    }
}
