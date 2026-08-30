using System.Text;
using System.Security.Cryptography.X509Certificates;

namespace AccountingAI.Core.Phc;

public static class PhcAtCertificatePasswordReader
{
    private const string CertificatePasswordParameter = "ge_atcertpass";

    public static string TryRead(
        string phcDirectory,
        string databaseName,
        string pfxPath)
    {
        if (!IsPhcCertificate(pfxPath) || string.IsNullOrWhiteSpace(databaseName))
        {
            return string.Empty;
        }

        foreach (var path in CandidateParameterFiles(phcDirectory, databaseName, pfxPath))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var value = ReadParameter(path, CertificatePasswordParameter);
                if (!string.IsNullOrEmpty(value) && CanOpenCertificate(pfxPath, value))
                {
                    return value;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // A introdução manual continua disponível quando uma instalação PHC não pode ser lida.
            }
        }

        return string.Empty;
    }

    private static bool IsPhcCertificate(string pfxPath) =>
        Path.GetFileName(pfxPath).Equals("certifphc.pfx", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> CandidateParameterFiles(
        string phcDirectory,
        string databaseName,
        string pfxPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddInstallationRoots(roots, phcDirectory);
        AddInstallationRoots(roots, Path.GetDirectoryName(pfxPath) ?? string.Empty);

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var companyPath = Path.Combine(root, databaseName.Trim(), "T_Para1.DBF");
            if (yielded.Add(companyPath))
            {
                yield return companyPath;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var parameterPath = Path.Combine(directory, "T_Para1.DBF");
                if (yielded.Add(parameterPath))
                {
                    yield return parameterPath;
                }
            }
        }
    }

    private static bool CanOpenCertificate(string pfxPath, string password)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                pfxPath,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
            return certificate.HasPrivateKey;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static void AddInstallationRoots(HashSet<string> roots, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var fullPath = Path.GetFullPath(directory);
        roots.Add(fullPath);
        var leaf = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parent = Directory.GetParent(fullPath)?.FullName;
        if (parent is null)
        {
            return;
        }

        if (leaf.Equals("_LOC", StringComparison.OrdinalIgnoreCase))
        {
            roots.Add(parent);
        }
        else if (leaf.EndsWith("_LOC", StringComparison.OrdinalIgnoreCase))
        {
            roots.Add(Path.Combine(parent, leaf[..^4]));
        }
    }

    private static string ReadParameter(string path, string parameterName)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 33)
        {
            throw new InvalidDataException("A tabela de parâmetros PHC é demasiado pequena.");
        }

        var recordCount = BitConverter.ToInt32(bytes, 4);
        var headerLength = BitConverter.ToUInt16(bytes, 8);
        var recordLength = BitConverter.ToUInt16(bytes, 10);
        if (headerLength < 33 || recordLength < 2 || headerLength > bytes.Length)
        {
            throw new InvalidDataException("O cabeçalho da tabela de parâmetros PHC é inválido.");
        }

        var fields = ReadFields(bytes, headerLength);
        var description = fields.SingleOrDefault(field =>
            field.Name.Equals("DESCRICAO", StringComparison.OrdinalIgnoreCase));
        var value = fields.SingleOrDefault(field =>
            field.Name.Equals("VALOR", StringComparison.OrdinalIgnoreCase));
        if (description is null || value is null)
        {
            throw new InvalidDataException("A tabela de parâmetros PHC não contém DESCRICAO e VALOR.");
        }

        for (var index = 0; index < recordCount; index++)
        {
            var recordOffset = headerLength + (index * recordLength);
            if (recordOffset + recordLength > bytes.Length)
            {
                throw new InvalidDataException("A tabela de parâmetros PHC terminou antes do último registo.");
            }

            if (bytes[recordOffset] == (byte)'*')
            {
                continue;
            }

            if (ReadText(bytes, recordOffset + description.Offset, description.Length)
                .Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return ReadText(bytes, recordOffset + value.Offset, value.Length);
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<DbfField> ReadFields(byte[] bytes, int headerLength)
    {
        var fields = new List<DbfField>();
        var fieldOffset = 32;
        var dataOffset = 1;
        while (fieldOffset + 32 <= headerLength && bytes[fieldOffset] != 0x0D)
        {
            var nameLength = Array.IndexOf(bytes, (byte)0, fieldOffset, 11) - fieldOffset;
            if (nameLength < 0)
            {
                nameLength = 11;
            }

            var name = Encoding.ASCII.GetString(bytes, fieldOffset, nameLength).Trim();
            var length = bytes[fieldOffset + 16];
            if (name.Length == 0 || length == 0)
            {
                throw new InvalidDataException("A tabela de parâmetros PHC contém um campo inválido.");
            }

            fields.Add(new DbfField(name, dataOffset, length));
            dataOffset += length;
            fieldOffset += 32;
        }

        return fields;
    }

    private static string ReadText(byte[] bytes, int offset, int length) =>
        Encoding.Latin1.GetString(bytes, offset, length).Trim('\0', ' ');

    private sealed record DbfField(string Name, int Offset, int Length);
}
