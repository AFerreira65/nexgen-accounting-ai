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
    public void TheCertificateStoredInTheDatabaseWorksWithoutAnyFileOnThisStation()
    {
        var (path, password) = WriteSelfSignedPfx();
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);

        var stored = new AtGeneralSettingsSnapshot("TesteWebservices.pfx", bytes, null, null, protector.Protect(password));
        // A secção At aponta para outro certificado, com outra senha: não conta, havendo configuração geral.
        var provider = Provider(new AtOptions { Certificate = { Path = "C:\\nao-existe\\outro.pfx", Password = "outra-senha" } }, stored);

        var status = provider.Describe();

        Assert.IsTrue(status.Valid, status.Problem);
        Assert.AreEqual("geral", status.Source);
        Assert.IsTrue(status.HasPrivateKey);
    }

    [TestMethod]
    public async Task TheCertificateFromTheDatabaseCanAuthenticateTheClientInATlsConnection()
    {
        // No Windows, uma chave privada só em memória (modo efémero) não serve para a autenticação do
        // cliente em TLS («As credenciais fornecidas ao pacote não foram reconhecidas»): foi assim que a
        // primeira ligação à AT falhou. Uma ligação TLS local com certificado de cliente apanha-o.
        var (path, password) = WriteSelfSignedPfx();
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        var provider = Provider(new AtOptions(), new AtGeneralSettingsSnapshot("a.pfx", bytes, null, null, protector.Protect(password)));
        Assert.IsTrue(provider.TryGetCertificate(out var clientCertificate));
        using var client = clientCertificate;

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx, "x"), "x");

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync();
                using var ssl = new System.Net.Security.SslStream(accepted.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                });
                return ssl.RemoteCertificate?.Subject;
            });

            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(System.Net.IPAddress.Loopback, port);
            using var clientSsl = new System.Net.Security.SslStream(tcp.GetStream(), false);
            await clientSsl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ClientCertificates = [client],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            });

            StringAssert.Contains(await server.WaitAsync(TimeSpan.FromSeconds(20)) ?? string.Empty, "NexGen Accounting AI Teste");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public void ThePasswordProtectedOnAnotherStationOpensTheCertificateHere()
    {
        var (path, password) = WriteSelfSignedPfx();
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);

        // A senha foi cifrada noutro posto, com outro chaveiro: este posto decifra-a na mesma.
        var otherStation = AtSecretProtector.Create(Path.Combine(Path.GetTempPath(), $"at-keys-{Guid.NewGuid():N}"));
        var stored = new AtGeneralSettingsSnapshot("a.pfx", bytes, null, null, otherStation.Protect(password));

        var status = Provider(new AtOptions(), stored).Describe();

        Assert.IsTrue(status.Valid, status.Problem);
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
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);

        // Certificado geral com outra senha guardada, e a certa só na secção At: não se mistura.
        var stored = new AtGeneralSettingsSnapshot("a.pfx", bytes, null, null, protector.Protect("outra-senha"));
        var status = Provider(new AtOptions { Certificate = { Password = password } }, stored).Describe();

        Assert.IsFalse(status.Valid);
        Assert.AreEqual("geral", status.Source);
        StringAssert.Contains(status.Problem!, "senha");
    }

    [TestMethod]
    public void DescribesThePublicKeyFromTheGeneralSettingsOrTheFile()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Chave Cifra Publica AT Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var bytes = certificate.Export(X509ContentType.Cert);
        var path = Path.Combine(Path.GetTempPath(), $"at-key-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(path, bytes);
        try
        {
            Assert.IsFalse(Provider(new AtOptions()).DescribePublicKey().Configured);

            var general = Provider(new AtOptions(), new AtGeneralSettingsSnapshot(null, null, "ChaveCifraPublicaAT2027.cer", bytes, null)).DescribePublicKey();
            Assert.IsTrue(general.Valid, general.Problem);
            Assert.AreEqual("geral", general.Source);
            Assert.AreEqual("ChaveCifraPublicaAT2027.cer", general.FileName);
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

    [TestMethod]
    public void ValidatesTheCertificateFileWithItsPasswordBeforeItIsStored()
    {
        var (path, password) = WriteSelfSignedPfx();
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        var provider = Provider(new AtOptions());

        var info = provider.ValidateCertificate(bytes, password);
        StringAssert.Contains(info.Subject, "NexGen Accounting AI Teste");

        var wrong = Assert.ThrowsExactly<ArgumentException>(() => provider.ValidateCertificate(bytes, "senha-errada"));
        StringAssert.Contains(wrong.Message, "senha");
        Assert.ThrowsExactly<ArgumentException>(() => provider.ValidateCertificate([1, 2, 3], password));

        // Só o certificado público, sem a chave privada, não serve para o transporte.
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Sem chave privada", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        Assert.ThrowsExactly<ArgumentException>(() => provider.ValidateCertificate(certificate.Export(X509ContentType.Cert), string.Empty));
    }

    [TestMethod]
    public void ValidatesThePublicKeyFileBeforeItIsStored()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Chave AT", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var provider = Provider(new AtOptions());

        StringAssert.Contains(provider.ValidatePublicKey(certificate.Export(X509ContentType.Cert)).Subject, "Chave AT");
        Assert.ThrowsExactly<ArgumentException>(() => provider.ValidatePublicKey([1, 2, 3, 4]));
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
