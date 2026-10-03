namespace AccountingAI.Application.Saft;

/// <summary>
/// Quem produz o ficheiro: o que o cabeçalho do SAF-T pede sobre o programa.
/// Um programa de contabilidade não tem número de certificado, por isso
/// <c>SoftwareCertificateNumber</c> é zero.
/// </summary>
public static class SaftProducer
{
    public const string CompanyTaxId = "518104486";

    public const string ProductId = "Accounting AI/RumaoAlgarismo - Unipessoal Lda";

    public const int SoftwareCertificateNumber = 0;
}

/// <summary>O cabeçalho de um SAF-T (PT) de contabilidade.</summary>
public sealed record SaftExportHeader(
    string TaxRegistrationNumber,
    string CompanyName,
    string AddressDetail,
    string City,
    string PostalCode,
    string Country,
    int FiscalYear,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly DateCreated,
    string ProductVersion);

/// <summary>
/// Uma conta do plano com os saldos de abertura e de fecho já separados em
/// devedor e credor. <see cref="GroupingCategory"/> é <c>GR</c> (1.º grau),
/// <c>GA</c> (agregadora) ou <c>GM</c> (movimento).
/// </summary>
public sealed record SaftExportAccount(
    string AccountId,
    string Description,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal ClosingDebit,
    decimal ClosingCredit,
    string GroupingCategory,
    string? GroupingCode,
    int? TaxonomyCode);

/// <summary>Um cliente ou fornecedor de <c>MasterFiles</c>.</summary>
public sealed record SaftExportParty(
    bool IsCustomer,
    string PartyId,
    string AccountId,
    string TaxId,
    string Name,
    string Country,
    string? AddressDetail = null,
    string? City = null,
    string? PostalCode = null);

public sealed record SaftExportLine(
    string RecordId,
    string AccountId,
    DateTime SystemEntryDate,
    string Description,
    decimal Debit,
    decimal Credit);

public sealed record SaftExportTransaction(
    string TransactionId,
    int Period,
    DateOnly TransactionDate,
    string SourceId,
    string Description,
    string DocArchivalNumber,
    string TransactionType,
    DateOnly GlPostingDate,
    string? CustomerId,
    string? SupplierId,
    IReadOnlyList<SaftExportLine> Lines);

public sealed record SaftExportJournal(string JournalId, string Description, IReadOnlyList<SaftExportTransaction> Transactions);

/// <summary>Tudo o que entra num SAF-T (PT) de contabilidade, pronto a escrever.</summary>
public sealed record SaftExportModel(
    SaftExportHeader Header,
    string TaxonomyReference,
    IReadOnlyList<SaftExportAccount> Accounts,
    IReadOnlyList<SaftExportParty> Parties,
    IReadOnlyList<SaftExportJournal> Journals)
{
    public int NumberOfEntries => Journals.Sum(journal => journal.Transactions.Count);

    public decimal TotalDebit => Journals.Sum(journal => journal.Transactions.Sum(item => item.Lines.Sum(line => line.Debit)));

    public decimal TotalCredit => Journals.Sum(journal => journal.Transactions.Sum(item => item.Lines.Sum(line => line.Credit)));
}

/// <summary>Um problema encontrado ao preparar a exportação.</summary>
/// <param name="Blocking">Se o ficheiro ficaria inválido face ao esquema ou às regras da Portaria n.º 302/2016.</param>
public sealed record SaftExportIssue(bool Blocking, string Message, int Count = 1);

/// <summary>
/// O que a exportação de um exercício produziria: as contagens, os totais e os
/// problemas. Com problemas bloqueantes o ficheiro não é gerado.
/// </summary>
public sealed record SaftExportReport(
    int FiscalYear,
    int AccountCount,
    int PartyCount,
    int TransactionCount,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<SaftExportIssue> Issues)
{
    public bool CanExport => Issues.All(issue => !issue.Blocking);
}

/// <summary>
/// Fronteira para a escrita do XML do SAF-T (PT) de contabilidade, versão
/// 1.04_01. A implementação não valida o conteúdo: escreve o modelo que recebe.
/// </summary>
public interface ISaftAccountingWriter
{
    Task WriteAsync(SaftExportModel model, Stream output, CancellationToken cancellationToken = default);
}
