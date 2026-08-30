using AccountingAI.Domain.Saft;

namespace AccountingAI.Application.Saft;

/// <summary>
/// Fronteira para leitura do cabeçalho de um ficheiro SAF-T (PT). A implementação
/// lê apenas o elemento <c>Header</c> e não materializa o resto do ficheiro.
/// </summary>
public interface ISaftHeaderReader
{
    Task<SaftHeader> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <summary>Lançada quando o conteúdo não é um SAF-T legível ou não tem um <c>Header</c> utilizável.</summary>
public sealed class SaftFileFormatException(string message) : Exception(message);
