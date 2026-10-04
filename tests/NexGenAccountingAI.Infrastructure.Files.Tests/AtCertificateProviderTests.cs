using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NexGenAccountingAI.Infrastructure.At;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class AtCertificateProviderTests
{
    private string keyRing = string.Empty;
    private AtSecretProtector protector = null!;

    [TestInitialize]
    public void SetUp()
    {
        keyRing = Path.Combine(Path.GetTempPath(), $"at-keys-{Guid.NewGuid():N}");
        protector = AtSecretProtector.Create(keyRing);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(keyRing))
        {
            Directory.Delete(keyRing, recursive: true);
        }
    }

    [TestMethod]
    public void ReportsNotConfiguredWhenThereIsNoCertificatePath()
    {
        var provider = new AtCertificateProvider(Options.Create(new AtOptions()), protector);

        var status = provider.Describe();

        Assert.IsFalse(status.Configured);
        Assert.IsFalse(status.Valid);
        Assert.AreEqual("Testes", status.Environment);
        StringAssert.Contains(status.Problem!, "Não há certificado");
    }

    [TestMethod]
    public void DescribesAValidCertificateWithoutExposingTheSecret()
    {
        var (path, password) = WriteSelfSignedPfx();
        try
        {
            var provider = new AtCertificateProvider(Options.Create(new AtOptions
            {
                Environment = "producao",
                Certificate = { Path = path, Password = password },
            }), protector);

            var status = provider.Describe();

            Assert.IsTrue(status.Configured);
            Assert.IsTrue(status.Valid);
            Assert.IsTrue(status.HasPrivateKey);
            Assert.AreEqual("Producao", status.Environment, "O ambiente é normalizado.");
            StringAssert.Contains(status.Subject!, "NexGen Accounting AI Teste");
            Assert.IsNull(status.Problem);
            Assert.IsFalse(status.Thumbprint!.Contains(password, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadsTheCertificateWhenThePasswordIsEncryptedInTheConfiguration()
    {
        var (path, password) = WriteSelfSignedPfx();
        try
        {
            var encrypted = protector.Protect(password);
            Assert.IsTrue(protector.IsProtected(encrypted));
            Assert.AreNotEqual(password, encrypted);

            var provider = new AtCertificateProvider(Options.Create(new AtOptions
            {
                Certificate =
                {
                    Directory = protector.Protect(Path.GetDirectoryName(path)!),
                    FileName = Path.GetFileName(path),
                    Password = encrypted,
                },
            }), protector);

            var status = provider.Describe();

            Assert.IsTrue(status.Valid, status.Problem);
            Assert.IsTrue(status.HasPrivateKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReportsAProblemForAWrongPasswordAndForAMissingFile()
    {
        var (path, _) = WriteSelfSignedPfx();
        try
        {
            var wrongPassword = new AtCertificateProvider(Options.Create(new AtOptions
            {
                Certificate = { Path = path, Password = "senha-errada" },
            }), protector).Describe();
            Assert.IsTrue(wrongPassword.Configured);
            Assert.IsFalse(wrongPassword.Valid);
            StringAssert.Contains(wrongPassword.Problem!, "senha");

            var missing = new AtCertificateProvider(Options.Create(new AtOptions
            {
                Certificate =
                {
                    Directory = Path.GetTempPath(),
                    FileName = $"nao-existe-{Guid.NewGuid():N}.pfx",
                },
            }), protector).Describe();
            Assert.IsTrue(missing.Configured);
            Assert.IsFalse(missing.Valid);
            StringAssert.Contains(missing.Problem!, "não existe");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (string Path, string Password) WriteSelfSignedPfx()
    {
        const string password = "senha-de-teste";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=NexGen Accounting AI Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(Path.GetTempPath(), $"at-cert-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return (path, password);
    }
}
