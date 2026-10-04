namespace NexGenAccountingAI.Application.ManagementReports;

/// <summary>
/// Monta o <see cref="ManagementReportDossier"/> de uma empresa para um período,
/// de forma determinística e a partir dos serviços de leitura. Não expõe EF nem
/// SQL. Devolve <c>null</c> quando a empresa não existe ou o utilizador não lhe
/// tem acesso.
/// </summary>
public interface IManagementReportDataService
{
    Task<ManagementReportDossier?> BuildDossierAsync(
        Guid userId,
        Guid companyId,
        ManagementReportRequest request,
        CancellationToken cancellationToken = default);
}
