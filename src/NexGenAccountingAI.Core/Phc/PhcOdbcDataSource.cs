using System.Data.Odbc;
using NexGenAccountingAI.Core.Configuration;

namespace NexGenAccountingAI.Core.Phc;

public sealed record PhcOdbcTestResult(bool Success, string Message, string ServerVersion = "", string Database = "");

public sealed record PhcClient(long Number, string Name, string TaxRegistrationNumber)
{
    public override string ToString() => $"{Number} — {Name} — NIF {TaxRegistrationNumber}";
}

public sealed record PhcArticle(string Reference, string Designation)
{
    public string DisplayName => Designation.Length == 0 ? Reference : $"{Reference} — {Designation}";

    public override string ToString() => DisplayName;
}

public sealed record PhcDocumentType(
    long Number,
    string Name,
    string SaftType,
    bool Closed,
    bool ImportSeries,
    string SourceTable)
{
    public string Key => $"{SourceTable}\u001f{Number}";

    public string DisplayName
    {
        get
        {
            var type = SaftType.Length == 0 ? string.Empty : $" — SAF-T {SaftType}";
            var state = Closed ? " — fechada" : string.Empty;
            return $"{Number} — {Name}{type}{state}";
        }
    }

    public override string ToString()
        => DisplayName;
}

public sealed record PhcDocumentCatalog(string SourceTable, IReadOnlyList<PhcDocumentType> Documents);

public sealed class PhcOdbcDataSource
{
    public async Task<PhcOdbcTestResult> TestConnectionAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new OdbcConnection(BuildConnectionString(company, credential));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 10;
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return new PhcOdbcTestResult(
                true,
                $"Ligação efetuada com sucesso ao ODBC {company.OdbcName}, base de dados {connection.Database}.",
                connection.ServerVersion,
                connection.Database);
        }
        catch (Exception exception) when (exception is OdbcException or InvalidOperationException or ArgumentException)
        {
            return new PhcOdbcTestResult(
                false,
                $"Não foi possível iniciar sessão no ODBC {company.OdbcName}, base de dados {company.DatabaseName}: {exception.Message}");
        }
    }

    public async Task<IReadOnlyList<PhcClient>> GetClientsAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new OdbcConnection(BuildConnectionString(company, credential));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT no, nome, ncont FROM cl ORDER BY nome, no";
        command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var clients = new List<PhcClient>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var number = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
            var name = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1))?.Trim() ?? string.Empty;
            var taxId = reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2))?.Trim() ?? string.Empty;
            clients.Add(new PhcClient(number, name, taxId));
        }

        return clients;
    }

    public async Task<IReadOnlyList<PhcArticle>> GetStockArticlesAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new OdbcConnection(BuildConnectionString(company, credential));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ref, design FROM st ORDER BY ref";
        command.CommandTimeout = 60;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var articles = new List<PhcArticle>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var reference = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0))?.Trim() ?? string.Empty;
            var designation = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1))?.Trim() ?? string.Empty;
            if (reference.Length > 0)
            {
                articles.Add(new PhcArticle(reference, designation));
            }
        }

        return articles;
    }

    public Task<PhcDocumentCatalog> GetInvoiceDocumentTypesAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default) =>
        GetDocumentTypesAsync(company, credential, ["TD"], ["TIPOSAFT"], cancellationToken);

    public Task<PhcDocumentCatalog> GetReceiptDocumentTypesAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        CancellationToken cancellationToken = default) =>
        GetDocumentTypesAsync(company, credential, ["TSRE", "RETD"], ["TIPOREAO", "TIPOSAFT"], cancellationToken);

    private static async Task<PhcDocumentCatalog> GetDocumentTypesAsync(
        PhcCompanyConfiguration company,
        PhcOdbcCredential credential,
        IReadOnlyList<string> tableNames,
        IReadOnlyList<string> typeFieldNames,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var tableName in tableNames)
        {
            try
            {
                await using var connection = new OdbcConnection(BuildConnectionString(company, credential));
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM {tableName}";
                command.CommandTimeout = 30;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var columns = Enumerable.Range(0, reader.FieldCount)
                    .ToDictionary(reader.GetName, index => index, StringComparer.OrdinalIgnoreCase);
                if (!columns.TryGetValue("NDOC", out var numberOrdinal) || !columns.TryGetValue("NMDOC", out var nameOrdinal))
                {
                    throw new InvalidDataException($"A tabela {tableName} não contém os campos NDOC e NMDOC esperados.");
                }

                var typeOrdinal = typeFieldNames
                    .Where(columns.ContainsKey)
                    .Select(field => columns[field])
                    .Cast<int?>()
                    .FirstOrDefault();
                var closedOrdinal = columns.TryGetValue("FECHADA", out var closed) ? closed : (int?)null;
                var importOrdinal = columns.TryGetValue("DOCSIMPORT", out var imported) ? imported : (int?)null;
                var documents = new List<PhcDocumentType>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var number = reader.IsDBNull(numberOrdinal)
                        ? 0L
                        : Convert.ToInt64(reader.GetValue(numberOrdinal), System.Globalization.CultureInfo.InvariantCulture);
                    var name = reader.IsDBNull(nameOrdinal)
                        ? string.Empty
                        : Convert.ToString(reader.GetValue(nameOrdinal))?.Trim() ?? string.Empty;
                    if (number == 0 || name.Length == 0)
                    {
                        continue;
                    }

                    documents.Add(new PhcDocumentType(
                        number,
                        name,
                        ReadString(reader, typeOrdinal),
                        ReadBoolean(reader, closedOrdinal),
                        ReadBoolean(reader, importOrdinal),
                        tableName));
                }

                return new PhcDocumentCatalog(
                    tableName,
                    documents.OrderBy(item => item.Number).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            }
            catch (Exception exception) when (exception is OdbcException or InvalidDataException)
            {
                lastError = exception;
            }
        }

        throw new InvalidDataException(
            $"Não foi possível ler a configuração PHC nas tabelas {string.Join(" ou ", tableNames)}.",
            lastError);
    }

    private static string ReadString(System.Data.Common.DbDataReader reader, int? ordinal) =>
        ordinal is null || reader.IsDBNull(ordinal.Value)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal.Value))?.Trim() ?? string.Empty;

    private static bool ReadBoolean(System.Data.Common.DbDataReader reader, int? ordinal)
    {
        if (ordinal is null || reader.IsDBNull(ordinal.Value))
        {
            return false;
        }

        var value = reader.GetValue(ordinal.Value);
        return value is bool boolean
            ? boolean
            : Convert.ToString(value)?.Trim() is "1" or "T" or "Y" or "S";
    }

    public static string BuildConnectionString(PhcCompanyConfiguration company, PhcOdbcCredential credential)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(company.OdbcName) || string.IsNullOrWhiteSpace(company.DatabaseName))
        {
            throw new ArgumentException("A empresa PHC deve ter ODBC e base de dados definidos.", nameof(company));
        }

        if (string.IsNullOrWhiteSpace(credential.Username) || credential.Password.Length == 0)
        {
            throw new ArgumentException("Indique o utilizador e a palavra-passe do ODBC.", nameof(credential));
        }

        var builder = new OdbcConnectionStringBuilder
        {
            ["DSN"] = company.OdbcName,
            ["DATABASE"] = company.DatabaseName,
            ["UID"] = credential.Username,
            ["PWD"] = credential.Password,
            ["Connection Timeout"] = 15
        };
        return builder.ConnectionString;
    }
}
