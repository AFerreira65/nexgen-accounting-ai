using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AccountingAI.Infrastructure.At;
using Microsoft.Extensions.Options;

namespace AccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class AtCertificateProviderTests
{
    [TestMethod]
    public void ReportsNotConfiguredWhenThereIsNoCertificatePath()
    {
        var provider = new AtCertificateProvider(Options.Create(new AtOptions()));

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
            }));

            var status = provider.Describe();

            Assert.IsTrue(status.Configured);
            Assert.IsTrue(status.Valid);
            Assert.IsTrue(status.HasPrivateKey);
            Assert.AreEqual("Producao", status.Environment, "O ambiente é normalizado.");
            StringAssert.Contains(status.Subject!, "Accounting AI Teste");
            Assert.IsNull(status.Problem);
            Assert.IsFalse(status.Thumbprint!.Contains(password, StringComparison.OrdinalIgnoreCase));
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
            })).Describe();
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
            })).Describe();
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
            "CN=Accounting AI Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(Path.GetTempPath(), $"at-cert-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return (path, password);
    }
}
