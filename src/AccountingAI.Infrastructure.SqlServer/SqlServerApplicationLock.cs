using System.Data;
using System.Globalization;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AccountingAI.Infrastructure.SqlServer;

internal static class SqlServerApplicationLock
{
    public static async Task AcquireAsync(
        AccountingDbContext context,
        string resource,
        CancellationToken cancellationToken)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A reserva da série exige uma transação ativa.");
        var connection = context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;
            SELECT @result;
            """;

        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.DbType = DbType.String;
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@lockTimeout";
        timeoutParameter.DbType = DbType.Int32;
        timeoutParameter.Value = 15_000;
        command.Parameters.Add(timeoutParameter);

        var rawResult = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var result = Convert.ToInt32(rawResult, CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new TimeoutException("Não foi possível reservar a série dentro do tempo disponível. Tente novamente.");
        }
    }
}
