using System.Buffers.Binary;
using System.Text;

namespace NexGenAccountingAI.Core.Phc;

public sealed record PhcCompanyCatalogEntry(string Name, string DatabaseName, string OdbcName);

public sealed record PhcCompanyCatalog(string FilePath, IReadOnlyList<PhcCompanyCatalogEntry> Companies);

public sealed class PhcCompanyCatalogReader
{
    private static readonly string[] RequiredFields = ["NOME", "DATAB", "ODBC"];

    public PhcCompanyCatalog Read(string phcPath)
    {
        var filePath = ResolveEpPath(phcPath);
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 33)
        {
            throw new InvalidDataException("O ficheiro EP.DBF é demasiado pequeno para ser válido.");
        }

        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2));
        if (recordCount < 0 || headerLength < 33 || recordLength < 2 || headerLength > bytes.Length)
        {
            throw new InvalidDataException("O cabeçalho do ficheiro EP.DBF é inválido.");
        }

        var fields = ReadFields(bytes, headerLength);
        foreach (var requiredField in RequiredFields)
        {
            if (!fields.Any(field => field.Name.Equals(requiredField, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"O ficheiro EP.DBF não contém o campo obrigatório {requiredField}.");
            }
        }

        var companies = new List<PhcCompanyCatalogEntry>();
        for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            var recordOffset = headerLength + (recordIndex * recordLength);
            if (recordOffset < 0 || recordOffset + recordLength > bytes.Length)
            {
                throw new InvalidDataException("O ficheiro EP.DBF terminou antes do último registo declarado.");
            }

            if (bytes[recordOffset] == (byte)'*')
            {
                continue;
            }

            var values = ReadRecord(bytes, recordOffset, recordLength, fields);
            var name = Value(values, "NOME");
            var database = Value(values, "DATAB");
            var odbc = Value(values, "ODBC");
            if (name.Length == 0 && database.Length == 0 && odbc.Length == 0)
            {
                continue;
            }

            companies.Add(new PhcCompanyCatalogEntry(name, database, odbc));
        }

        return new PhcCompanyCatalog(filePath, companies);
    }

    public static string ResolveEpPath(string phcPath)
    {
        if (string.IsNullOrWhiteSpace(phcPath))
        {
            throw new ArgumentException("Indique a pasta da instalação PHC.", nameof(phcPath));
        }

        var fullPath = Path.GetFullPath(phcPath.Trim());
        if (File.Exists(fullPath) && Path.GetFileName(fullPath).Equals("ep.dbf", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var candidates = new[]
        {
            Path.Combine(fullPath, "EP.DBF"),
            Path.Combine(fullPath, "_loc", "EP.DBF")
        };
        var match = candidates.FirstOrDefault(File.Exists);
        return match ?? throw new FileNotFoundException(
            $"Não foi encontrado EP.DBF em {fullPath} nem na subpasta _loc.",
            candidates[0]);
    }

    private static List<DbfField> ReadFields(byte[] bytes, int headerLength)
    {
        var fields = new List<DbfField>();
        for (var offset = 32; offset + 31 < headerLength; offset += 32)
        {
            if (bytes[offset] == 0x0D)
            {
                break;
            }

            var nameLength = 0;
            while (nameLength < 11 && bytes[offset + nameLength] != 0)
            {
                nameLength++;
            }

            var name = Encoding.ASCII.GetString(bytes, offset, nameLength).Trim();
            var length = bytes[offset + 16];
            if (name.Length == 0 || length == 0)
            {
                throw new InvalidDataException("O EP.DBF contém uma definição de campo inválida.");
            }

            fields.Add(new DbfField(name, length));
        }

        return fields;
    }

    private static Dictionary<string, string> ReadRecord(
        byte[] bytes,
        int recordOffset,
        int recordLength,
        IEnumerable<DbfField> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cursor = recordOffset + 1;
        var end = recordOffset + recordLength;
        foreach (var field in fields)
        {
            if (cursor + field.Length > end)
            {
                throw new InvalidDataException("Um registo do EP.DBF não respeita a estrutura declarada.");
            }

            result[field.Name] = Encoding.Latin1.GetString(bytes, cursor, field.Length)
                .Trim(' ', '\0');
            cursor += field.Length;
        }

        return result;
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private sealed record DbfField(string Name, int Length);
}
