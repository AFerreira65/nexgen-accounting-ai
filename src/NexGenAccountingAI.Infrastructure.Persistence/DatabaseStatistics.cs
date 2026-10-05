using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Atualiza as estatísticas do PostgreSQL depois de uma importação grande. Sem isto,
/// o planeador continua a julgar as tabelas quase vazias até o autovacuum passar, e
/// escolhe planos que, com um SAF-T anual (27 mil movimentos, 44 mil contas), levavam
/// minutos por consulta e penduravam a aplicação; com as estatísticas, milissegundos.
/// </summary>
internal static class DatabaseStatistics
{
    /// <summary>As tabelas que as importações de SAF-T enchem.</summary>
    private const string ImportTables =
        "accounting.\"Accounts\", accounting.\"JournalEntries\", accounting.\"JournalEntryLines\", accounting.\"Journals\", accounting.\"Parties\"";

    public static async Task RefreshAfterImportAsync(this AccountingDbContext context, CancellationToken cancellationToken)
    {
        // Só no PostgreSQL; noutros fornecedores (testes em memória) não há nada a fazer.
        if (context.Database.IsNpgsql())
        {
            await context.Database.ExecuteSqlRawAsync("ANALYZE " + ImportTables, cancellationToken).ConfigureAwait(false);
        }
    }
}
