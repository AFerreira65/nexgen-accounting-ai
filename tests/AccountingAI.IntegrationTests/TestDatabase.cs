using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace AccountingAI.IntegrationTests;

/// <summary>
/// Ligação ao PostgreSQL dos testes de integração. Cada teste cria (via migrações) e apaga uma
/// base descartável no servidor indicado em ACCOUNTINGAI_TEST_PG_CONNECTION, ou no PostgreSQL
/// local com o papel <c>accounting</c> (ver scripts/postgres/local-setup.sql).
/// </summary>
internal static class TestDatabase
{
    public const string ConnectionVariable = "ACCOUNTINGAI_TEST_PG_CONNECTION";

    private const string DefaultConnection = "Host=localhost;Port=5432;Username=accounting;Password=accounting";

    // O PostgreSQL limita os identificadores a 63 caracteres.
    private const int MaxDatabaseNameLength = 63;

    public static string ConnectionString(string feature)
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        var builder = new NpgsqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? DefaultConnection : configured);
        var suffix = $"_{Guid.NewGuid():N}";
        var prefix = $"acc_{feature.ToLowerInvariant()}";
        builder.Database = prefix[..Math.Min(prefix.Length, MaxDatabaseNameLength - suffix.Length)] + suffix;
        return builder.ConnectionString;
    }

    public static void Configure(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__EFMigrationsHistory", "infrastructure");

    /// <summary>
    /// Apaga a base descartável. O EF usa DROP DATABASE ... WITH (FORCE), que falha com
    /// 42501 se um processo de superutilizador (o autovacuum, tipicamente) estiver ligado à
    /// base nesse instante; volta-se a tentar até ele sair.
    /// </summary>
    public static async Task DropAsync(DbContext context)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
                return;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege && attempt < 10)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt)).ConfigureAwait(false);
            }
        }
    }
}
