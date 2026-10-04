namespace NexGenAccountingAI.Infrastructure.At;

/// <summary>Configuração da ligação à AT, ligada da secção <c>At</c>.</summary>
public sealed class AtOptions
{
    public const string SectionName = "At";

    /// <summary>Ambiente: <c>Testes</c> (por omissão) ou <c>Producao</c>.</summary>
    public string Environment { get; set; } = "Testes";

    public AtCertificateOptions Certificate { get; set; } = new();
}

/// <summary>
/// O certificado de transporte da AT. A <see cref="Password"/> vem só de
/// <em>user-secrets</em> ou da variável de ambiente <c>AT_CERTIFICATE_PASSWORD</c>
/// — nunca de <c>appsettings*.json</c> (ADR-0008).
/// </summary>
public sealed class AtCertificateOptions
{
    /// <summary>Pasta onde está o ficheiro PFX/P12.</summary>
    public string? Directory { get; set; }

    /// <summary>Nome do ficheiro PFX/P12.</summary>
    public string? FileName { get; set; }

    /// <summary>Caminho absoluto do ficheiro; tem prioridade sobre <see cref="Directory"/> + <see cref="FileName"/>.</summary>
    public string? Path { get; set; }

    /// <summary>Senha do certificado.</summary>
    public string? Password { get; set; }

    /// <summary>O caminho a usar, ou <c>null</c> quando não está configurado.</summary>
    public string? ResolvedPath =>
        !string.IsNullOrWhiteSpace(Path) ? Path
        : !string.IsNullOrWhiteSpace(Directory) && !string.IsNullOrWhiteSpace(FileName)
            ? System.IO.Path.Combine(Directory, FileName)
            : null;
}
