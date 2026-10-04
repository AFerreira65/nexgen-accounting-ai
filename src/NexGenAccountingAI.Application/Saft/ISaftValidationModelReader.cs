namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Percorre um SAF-T (PT) numa única passagem em streaming e devolve o
/// <see cref="SaftValidationModel"/> — o que as regras analíticas precisam,
/// sem materializar o ficheiro. Lança <see cref="SaftFileFormatException"/>
/// quando o conteúdo não é XML bem formado.
/// </summary>
public interface ISaftValidationModelReader
{
    Task<SaftValidationModel> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}
