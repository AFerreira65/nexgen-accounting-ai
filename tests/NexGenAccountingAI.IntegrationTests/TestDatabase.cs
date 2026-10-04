using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using NexGenAccountingAI.Tests.Shared;

namespace NexGenAccountingAI.IntegrationTests;

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

    /// <summary>Apaga a base descartável (ver <see cref="PostgresTestDatabaseDropper"/>).</summary>
    public static async Task DropAsync(DbContext context)
    {
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("O contexto de teste não tem ligação configurada.");
        await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        await PostgresTestDatabaseDropper.DropAsync(connectionString).ConfigureAwait(false);
    }
}
