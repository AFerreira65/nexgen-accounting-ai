namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Resultado da criação de uma empresa a partir de um SAF-T: a empresa e a
/// contagem de cadastros provisionados na mesma transação.
/// </summary>
public sealed record SaftCompanyCreationResult(
    Guid CompanyId,
    string LegalName,
    string TaxRegistrationNumber,
    string AccountingStandard,
    string? AccountingSetupVersion,
    int AccountCount,
    int JournalCount,
    int DocumentTypeCount);
