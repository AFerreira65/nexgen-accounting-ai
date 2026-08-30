using AccountingAI.Domain.Saft;

namespace AccountingAI.Application.Saft;

public enum SaftCompanyMatchOutcome
{
    /// <summary>Existe uma empresa com o NIF do ficheiro a que o utilizador tem acesso.</summary>
    ExistingCompany,

    /// <summary>Nenhuma empresa acessível tem o NIF do ficheiro; a criação será proposta.</summary>
    NewCompanyProposed
}

public sealed record SaftCompanyMatch(
    SaftCompanyMatchOutcome Outcome,
    Guid? CompanyId,
    string? LegalName,
    string? AccountingStandard,
    string? AccountingSetupVersion);

/// <summary>Resultado da inspeção de um ficheiro SAF-T: cabeçalho validado e correspondência de empresa por NIF. Não escreve nada.</summary>
public sealed record SaftInspectionResult(SaftHeader Header, SaftCompanyMatch Match);
