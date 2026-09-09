namespace AccountingAI.Application.Vat;

/// <summary>
/// Produz o <see cref="VatReturn"/> — o apuramento contabilístico de IVA — de
/// uma empresa num intervalo de datas. Só lê; exclui os lançamentos anulados.
/// Devolve <c>null</c> quando a empresa não existe ou o utilizador não lhe tem
/// acesso.
/// </summary>
public interface IVatReturnService
{
    Task<VatReturn?> BuildAsync(
        Guid userId,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
