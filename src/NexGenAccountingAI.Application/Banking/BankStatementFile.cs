namespace NexGenAccountingAI.Application.Banking;

/// <summary>
/// Um movimento de um extrato bancário. <see cref="Amount"/> tem sinal do ponto
/// de vista da empresa: positivo quando entra dinheiro na conta (crédito no
/// extrato), negativo quando sai.
/// </summary>
public sealed record BankStatementRow(
    int LineNumber,
    DateOnly? BookingDate,
    DateOnly? ValueDate,
    string Description,
    decimal Amount,
    decimal? Balance,
    string? Reference,
    string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Um extrato lido de um ficheiro: o formato reconhecido, o IBAN (quando o
/// ficheiro o traz), os saldos declarados e os movimentos.
/// </summary>
public sealed record BankStatementFile(
    string Format,
    string? Iban,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    DateOnly? ClosingDate,
    IReadOnlyList<BankStatementRow> Rows,
    IReadOnlyList<string> Warnings);

/// <summary>O ficheiro não é um extrato que se saiba ler.</summary>
public sealed class BankStatementFormatException(string message) : Exception(message);

/// <summary>
/// Lê um extrato bancário: ISO 20022 <c>camt.053</c> (XML) ou o CSV exportado
/// pelo banco, reconhecido pelos nomes das colunas.
/// </summary>
public interface IBankStatementReader
{
    Task<BankStatementFile> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}
