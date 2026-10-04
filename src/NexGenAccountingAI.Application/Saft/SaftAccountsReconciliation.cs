namespace NexGenAccountingAI.Application.Saft;

/// <summary>
/// Uma conta do plano geral lida do bloco <c>GeneralLedgerAccounts</c> de um SAF-T,
/// com os saldos de abertura declarados (0 quando ausentes).
/// </summary>
public sealed record SaftAccount(
    string Code,
    string Description,
    decimal OpeningDebit = 0m,
    decimal OpeningCredit = 0m);

/// <summary>Situação de uma conta do ficheiro face ao plano de contas da empresa.</summary>
public enum SaftAccountState
{
    /// <summary>O código já existe no plano da empresa.</summary>
    Existing,

    /// <summary>O código não existe no plano da empresa.</summary>
    New
}

/// <summary>Uma conta do ficheiro com a sua situação face ao plano da empresa.</summary>
public sealed record SaftAccountStatus(string Code, string Description, SaftAccountState State);

/// <summary>
/// Reconciliação do plano de contas de um SAF-T com o plano de uma empresa. Só
/// classifica; não cria nem altera contas.
/// </summary>
public sealed record SaftAccountsReconciliation(
    int FileAccountCount,
    int ExistingCount,
    int NewCount,
    int CompanyPostingAccountCount,
    IReadOnlyList<SaftAccountStatus> Accounts);

/// <summary>Uma conta criada na empresa a partir do plano de contas de um SAF-T.</summary>
public sealed record SaftAccountCreated(string Code, string Name, string? ParentCode, bool AllowsPosting);

/// <summary>
/// Resultado de importar o plano de contas de um SAF-T: cria as contas que ainda
/// não existem na empresa, na ordem hierárquica, e não toca nas existentes.
/// </summary>
public sealed record SaftAccountsImportResult(
    int CreatedCount,
    int AlreadyExistedCount,
    IReadOnlyList<SaftAccountCreated> Created);

/// <summary>
/// Resultado de importar os saldos de abertura do plano de contas de um SAF-T como
/// um lançamento no diário de abertura (tipo de documento <c>AB</c>), datado de 1
/// de janeiro do exercício. Só considera contas de movimento da empresa com saldo
/// de abertura não nulo. Não repete se já existir abertura para o exercício.
/// </summary>
public sealed record SaftOpeningBalancesImportResult(
    int LineCount,
    decimal TotalDebit,
    decimal TotalCredit,
    string? EntryReference,
    bool AlreadyImported);
