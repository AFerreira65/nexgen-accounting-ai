using Npgsql;

namespace NexGenAccountingAI.Tests.Shared;

/// <summary>
/// Apaga uma base descartável dos testes. Não usa o EnsureDeleted do EF: esse faz
/// DROP DATABASE ... WITH (FORCE), que tenta terminar todos os processos ligados e falha
/// com 42501 quando um deles é do superutilizador (o autovacuum, tipicamente). Um
/// DROP DATABASE simples manda o próprio PostgreSQL parar o autovacuum e espera por ele.
/// </summary>
internal static class PostgresTestDatabaseDropper
{
    public static async Task DropAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database
            ?? throw new InvalidOperationException("A ligação de teste não indica a base de dados.");
        builder.Database = "postgres";
        builder.Pooling = false;

        // As ligações em pool da própria aplicação também contam como "outros utilizadores".
        NpgsqlConnection.ClearAllPools();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS \"{database.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                return;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectInUse && attempt < 10)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt)).ConfigureAwait(false);
            }
        }
    }
}
