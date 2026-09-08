namespace AccountingAI.Application.CrossCheck;

/// <summary>
/// Produz o <see cref="CrossCheckReport"/> de uma empresa para um intervalo de
/// datas fechado. Devolve <c>null</c> quando a empresa não existe ou o
/// utilizador não lhe tem acesso.
/// </summary>
public interface ICrossCheckService
{
    Task<CrossCheckReport?> BuildAsync(
        Guid userId,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
