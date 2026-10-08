using System.Text;
using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>A chave da Anthropic na configuração geral (só o produtor): cifrada, para todas as empresas.</summary>
[TestClass]
public sealed class AiGeneralSettingsTests
{
    private sealed class Protector : IAtSecretProtector
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v2:", StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => "enc:v2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string Unprotect(string? value) => IsProtected(value) ? Encoding.UTF8.GetString(Convert.FromBase64String(value![7..])) : value ?? string.Empty;
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public async Task TheKeyIsSavedEncryptedValidatedAndSeenByTheWholeApplication()
    {
        var connection = TestDatabase.ConnectionString("AiKey");
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(connection, TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddScoped(_ => new AccountingDbContext(options));
            await using var provider = services.BuildServiceProvider();
            var clock = new Clock(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
            var source = new DatabaseAiKeySource(provider.GetRequiredService<IServiceScopeFactory>(), new Protector(), clock);
            var settings = new AiGeneralSettingsService(context, new Protector(), source);
            var none = CancellationToken.None;
            const string Key = "sk-ant-api03-ChaveDeTesteMuitoLongaParaOsTestes0123456789";

            Assert.IsNull(source.CurrentKey());
            Assert.IsFalse((await settings.StatusAsync(none).ConfigureAwait(false)).Configured);

            // Uma chave truncada, com espaços ou de outro serviço é recusada com uma explicação.
            foreach (var bad in new[] { "", "sk-ant-curta", "abc123456789012345678901234567890", "sk-ant-api03 com espaços no meio 0123456789" })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => settings.SaveKeyAsync(bad, none)).ConfigureAwait(false);
            }

            var status = await settings.SaveKeyAsync($"  {Key}\r\n", none).ConfigureAwait(false);
            Assert.IsTrue(status.Configured);
            Assert.AreEqual(Key[^8..], status.Hint, "O ecrã só reconhece o fim da chave.");
            Assert.AreEqual(Key, source.CurrentKey(), "Vale logo, sem esperar pela memória temporária.");

            var stored = await context.AiGeneralSettings.AsNoTracking().SingleAsync().ConfigureAwait(false);
            Assert.IsFalse(stored.EncryptedApiKey!.Contains("ChaveDeTeste", StringComparison.Ordinal), "A chave guarda-se cifrada.");
            Assert.IsTrue(stored.EncryptedApiKey.StartsWith("enc:v2:", StringComparison.Ordinal));

            // Outro posto (outra instância) lê a mesma chave da base de dados, ao fim da memória temporária.
            var other = new DatabaseAiKeySource(provider.GetRequiredService<IServiceScopeFactory>(), new Protector(), clock);
            Assert.AreEqual(Key, other.CurrentKey());

            // Substituir e retirar.
            const string Second = "sk-ant-api03-OutraChaveDeTesteMuitoLongaParaOsTestes987654";
            await settings.SaveKeyAsync(Second, none).ConfigureAwait(false);
            Assert.AreEqual(Second, source.CurrentKey());
            Assert.AreEqual(1, await context.AiGeneralSettings.CountAsync().ConfigureAwait(false), "Só há uma linha.");
            Assert.IsTrue(await settings.RemoveKeyAsync(none).ConfigureAwait(false));
            Assert.IsNull(source.CurrentKey());
            Assert.IsFalse((await settings.StatusAsync(none).ConfigureAwait(false)).Configured);
            Assert.IsFalse(await settings.RemoveKeyAsync(none).ConfigureAwait(false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
