namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Valida um ficheiro SAF-T (PT) contra o esquema XSD oficial da versão
/// declarada. A implementação lê em streaming e não materializa o ficheiro.
/// </summary>
public interface ISaftSchemaValidator
{
    /// <summary>
    /// Valida o conteúdo e devolve o relatório. Nunca lança por o XML ser
    /// válido mas não conforme — isso vem no relatório com
    /// <see cref="SaftValidationReport.SchemaValid"/> falso. Lança
    /// <see cref="SaftFileFormatException"/> só quando o conteúdo não é XML
    /// bem formado.
    /// </summary>
    Task<SaftValidationReport> ValidateAsync(Stream content, CancellationToken cancellationToken = default);
}
