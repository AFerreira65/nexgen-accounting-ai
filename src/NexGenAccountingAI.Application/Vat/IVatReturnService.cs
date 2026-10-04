namespace NexGenAccountingAI.Application.Vat;

/// <summary>
/// Produz o <see cref="VatReturn"/> — o apuramento contabilístico de IVA — de
/// uma empresa num intervalo de datas. Só lê; exclui os lançamentos anulados.
/// Devolve <c>null</c> quando a empresa não existe ou o utilizador não lhe tem
/// acesso.
/// </summary>
public interface IVatReturnService
{
    /// <param name="priorPeriodCredit">
    /// Quando indicado, é o excesso a reportar do período anterior a usar (e
    /// <see cref="VatReturn.PriorCreditAssumed"/> fica <c>false</c>); quando
    /// <c>null</c>, é assumido do saldo devedor das contas <c>2437*</c>.
    /// </param>
    Task<VatReturn?> BuildAsync(
        Guid userId,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        decimal? priorPeriodCredit = null,
        CancellationToken cancellationToken = default);
}
