using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NexGenAccountingAI.Infrastructure.At;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.At;

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
        var provider = Provider(new AtOptions());

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
            var provider = Provider(new AtOptions
            {
                Environment = "producao",
                Certificate = { Path = path, Password = password },
            });

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

            var provider = Provider(new AtOptions
            {
                Certificate =
                {
                    Directory = protector.Protect(Path.GetDirectoryName(path)!),
                    FileName = Path.GetFileName(path),
                    Password = encrypted,
                },
            });

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
            var wrongPassword = Provider(new AtOptions
            {
                Certificate = { Path = path, Password = "senha-errada" },
            }).Describe();
            Assert.IsTrue(wrongPassword.Configured);
            Assert.IsFalse(wrongPassword.Valid);
            StringAssert.Contains(wrongPassword.Problem!, "senha");

            var missing = Provider(new AtOptions
            {
                Certificate =
                {
                    Directory = Path.GetTempPath(),
                    FileName = $"nao-existe-{Guid.NewGuid():N}.pfx",
                },
            }).Describe();
            Assert.IsTrue(missing.Configured);
            Assert.IsFalse(missing.Valid);
            StringAssert.Contains(missing.Problem!, "não existe");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Um fornecedor com a secção At indicada e, opcionalmente, uma configuração geral guardada.</summary>
    private AtCertificateProvider Provider(AtOptions fileOptions, AtGeneralSettingsSnapshot? stored = null)
    {
        var options = Options.Create(fileOptions);
        var services = new ServiceCollection();
        services.AddSingleton<IAtGeneralSettingsReader>(new StoredSettings(stored));
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new AtCertificateProvider(options, new AtSettingsResolver(options, protector, TimeProvider.System, scopes));
    }

    private sealed class StoredSettings(AtGeneralSettingsSnapshot? snapshot) : IAtGeneralSettingsReader
    {
        public AtGeneralSettingsSnapshot? Read() => snapshot;
    }

    [TestMethod]
    public void TheGeneralSettingsReplaceTheConfigurationFileForTheCertificateAndItsPassword()
    {
        var (path, password) = WriteSelfSignedPfx();
        try
        {
            var stored = new AtGeneralSettingsSnapshot(
                Path.GetDirectoryName(path), Path.GetFileName(path), null, null, protector.Protect(password));
            // A secção At aponta para outro certificado, com outra senha: não conta, havendo configuração geral.
            var provider = Provider(new AtOptions { Certificate = { Path = "C:\\nao-existe\\outro.pfx", Password = "outra-senha" } }, stored);

            var status = provider.Describe();

            Assert.IsTrue(status.Valid, status.Problem);
            Assert.AreEqual("geral", status.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WithoutGeneralSettingsTheConfigurationFileStillWorksAndSaysSo()
    {
        var (path, password) = WriteSelfSignedPfx();
        try
        {
            var status = Provider(new AtOptions { Certificate = { Path = path, Password = password } }).Describe();

            Assert.IsTrue(status.Valid, status.Problem);
            Assert.AreEqual("ficheiro", status.Source);
            Assert.AreEqual("nenhuma", Provider(new AtOptions()).Describe().Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TheGeneralCertificateIsNotOpenedWithThePasswordOfTheConfigurationFile()
    {
        var (path, password) = WriteSelfSignedPfx();
        try
        {
            // Certificado geral sem senha guardada, e senha certa só na secção At: não se mistura.
            var stored = new AtGeneralSettingsSnapshot(Path.GetDirectoryName(path), Path.GetFileName(path), null, null, null);
            var status = Provider(new AtOptions { Certificate = { Password = password } }, stored).Describe();

            Assert.IsFalse(status.Valid);
            Assert.AreEqual("geral", status.Source);
            StringAssert.Contains(status.Problem!, "senha");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DescribesThePublicKeyFromTheGeneralSettingsOrTheFile()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Chave Cifra Publica AT Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(Path.GetTempPath(), $"at-key-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
        try
        {
            Assert.IsFalse(Provider(new AtOptions()).DescribePublicKey().Configured);

            var general = Provider(new AtOptions(), new AtGeneralSettingsSnapshot(null, null, Path.GetDirectoryName(path), Path.GetFileName(path), null)).DescribePublicKey();
            Assert.IsTrue(general.Valid, general.Problem);
            Assert.AreEqual("geral", general.Source);
            StringAssert.Contains(general.Subject!, "Chave Cifra Publica AT Teste");

            var fromFile = Provider(new AtOptions { PublicKey = { Path = path } }).DescribePublicKey();
            Assert.IsTrue(fromFile.Valid, fromFile.Problem);
            Assert.AreEqual("ficheiro", fromFile.Source);

            var missing = Provider(new AtOptions { PublicKey = { Path = path + ".nao" } }).DescribePublicKey();
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
