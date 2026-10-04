using System.Data;
using AccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AccountingAI.Infrastructure.Persistence;

internal static class DatabaseApplicationLock
{
    // Código SQLSTATE do PostgreSQL quando lock_timeout expira (lock_not_available).
    private const string LockNotAvailable = "55P03";

    public static async Task AcquireAsync(
        AccountingDbContext context,
        string resource,
        CancellationToken cancellationToken)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A reserva da série exige uma transação ativa.");
        var connection = context.Database.GetDbConnection();

        // Advisory lock exclusivo, libertado no fim da transação. O lock_timeout limita a espera
        // a 15s e é reposto logo a seguir para não afetar o resto da transação.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SET LOCAL lock_timeout = '15s';
            SELECT pg_advisory_xact_lock(hashtextextended(@resource, 0));
            SET LOCAL lock_timeout TO DEFAULT;
            """;

        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.DbType = DbType.String;
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == LockNotAvailable)
        {
            throw new TimeoutException("Não foi possível reservar a série dentro do tempo disponível. Tente novamente.", exception);
        }
    }
}
