using NexGenAccountingAI.Application.DataDictionary;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// Constrói o dicionário de dados a partir do modelo do EF Core (esquema,
/// tabelas, colunas, tipos e chaves) enriquecido com o texto pt-PT de
/// <see cref="DataDictionaryText"/>. Só lê metadados — não consulta a base.
/// </summary>
public sealed class DataDictionaryService(AccountingDbContext context)
{
    public IReadOnlyList<DataDictionaryTableSummary> ListTables() =>
        MappedEntityTypes()
            .Select(entityType =>
            {
                var (schema, table) = NameOf(entityType);
                return new DataDictionaryTableSummary(
                    schema, table, DataDictionaryText.PurposeOf(table), entityType.GetProperties().Count());
            })
            .OrderBy(item => item.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public DataDictionaryTable? GetTable(string schema, string table)
    {
        var entityType = MappedEntityTypes().FirstOrDefault(candidate =>
        {
            var (candidateSchema, candidateName) = NameOf(candidate);
            return string.Equals(candidateSchema, schema, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidateName, table, StringComparison.OrdinalIgnoreCase);
        });
        if (entityType is null)
        {
            return null;
        }

        var (resolvedSchema, resolvedName) = NameOf(entityType);
        var indexes = entityType.GetIndexes().ToList();

        var columns = entityType.GetProperties()
            .Select(property =>
            {
                // Numa FK composta (ex.: (CompanyId, JournalId) → Journals) só a última
                // coluna «aponta» para o principal; a coluna partilhada de empresa fica
                // como coluna indexada, não como FK.
                var containingForeignKeys = property.GetContainingForeignKeys().ToList();
                var pointingForeignKey = containingForeignKeys.FirstOrDefault(fk => fk.Properties.Count == 1)
                    ?? containingForeignKeys.FirstOrDefault(fk => fk.Properties[^1] == property);
                var coveringIndex = indexes.FirstOrDefault(index => index.Properties.Contains(property));

                return new DataDictionaryColumn(
                    property.GetColumnName(),
                    FriendlyClrType(property.ClrType),
                    property.GetColumnType() ?? property.FindRelationalTypeMapping()?.StoreType ?? "—",
                    property.IsNullable,
                    property.GetMaxLength(),
                    property.GetPrecision(),
                    property.GetScale(),
                    property.IsPrimaryKey(),
                    pointingForeignKey is not null,
                    pointingForeignKey is null ? null : ReferenceOf(pointingForeignKey.PrincipalEntityType),
                    coveringIndex?.IsUnique ?? false,
                    coveringIndex is not null,
                    DataDictionaryText.DescriptionOf(resolvedName, property.Name));
            })
            .OrderByDescending(column => column.PrimaryKey)
            .ThenBy(column => column.ForeignKey ? 0 : 1)
            .ToList();

        return new DataDictionaryTable(
            resolvedSchema, resolvedName, DataDictionaryText.PurposeOf(resolvedName), columns);
    }

    private IEnumerable<IEntityType> MappedEntityTypes() =>
        context.Model.GetEntityTypes().Where(entityType => entityType.GetTableName() is not null);

    private static (string Schema, string Table) NameOf(IEntityType entityType) =>
        (entityType.GetSchema() ?? "public", entityType.GetTableName()!);

    private static string ReferenceOf(IEntityType principal)
    {
        var (schema, table) = NameOf(principal);
        return $"{schema}.{table}";
    }

    private static string FriendlyClrType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var name = underlying switch
        {
            _ when underlying == typeof(string) => "string",
            _ when underlying == typeof(bool) => "bool",
            _ when underlying == typeof(int) => "int",
            _ when underlying == typeof(long) => "long",
            _ when underlying == typeof(decimal) => "decimal",
            _ when underlying == typeof(Guid) => "Guid",
            _ when underlying == typeof(DateOnly) => "DateOnly",
            _ when underlying == typeof(DateTime) => "DateTime",
            _ when underlying == typeof(DateTimeOffset) => "DateTimeOffset",
            _ when underlying == typeof(byte[]) => "byte[]",
            _ => underlying.Name,
        };
        return Nullable.GetUnderlyingType(type) is not null ? $"{name}?" : name;
    }
}
