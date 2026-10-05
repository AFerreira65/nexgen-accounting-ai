using System.Globalization;
using System.Text;

namespace NexGenAccountingAI.Domain.Banking;

/// <summary>
/// Um movimento de um extrato bancário importado para uma conta de depósitos
/// (11, 12 ou 13) de uma empresa. <see cref="Amount"/> é positivo quando entra
/// dinheiro e negativo quando sai — o mesmo sinal que débito menos crédito na
/// conta. Concilia-se por uma <see cref="BankReconciliation"/>, que junta um
/// ou mais movimentos a um ou mais lançamentos.
/// </summary>
public sealed class BankStatementLine
{
    public const int DescriptionMaxLength = 500;

    public const int ReferenceMaxLength = 500;

    public const int LineKeyMaxLength = 64;

    public const int EntryReferenceMaxLength = 48;

    private BankStatementLine()
    {
    }

    private BankStatementLine(Guid id, Guid companyId, Guid accountId, Guid importId, string lineKey)
    {
        Id = id;
        CompanyId = companyId;
        AccountId = accountId;
        ImportId = importId;
        LineKey = lineKey;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>A conta de depósitos (plano de contas) a que o extrato pertence.</summary>
    public Guid AccountId { get; private set; }

    public Guid ImportId { get; private set; }

    /// <summary>Chave de idempotência por conta: reimportar o mesmo extrato não duplica.</summary>
    public string LineKey { get; private set; } = string.Empty;

    public DateOnly BookingDate { get; private set; }

    public DateOnly? ValueDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    /// <summary>O saldo depois do movimento, quando o banco o indica.</summary>
    public decimal? Balance { get; private set; }

    public string? Reference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>A conciliação a que o movimento pertence (com um ou mais lançamentos), se houver.</summary>
    public Guid? ReconciliationId { get; private set; }

    public static BankStatementLine Create(
        Guid companyId,
        Guid accountId,
        Guid importId,
        string lineKey,
        DateOnly bookingDate,
        DateOnly? valueDate,
        string? description,
        decimal amount,
        decimal? balance,
        string? reference)
    {
        if (companyId == Guid.Empty || accountId == Guid.Empty)
        {
            throw new ArgumentException("A empresa e a conta são obrigatórias.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(lineKey);
        if (amount == 0m)
        {
            throw new ArgumentException("Um movimento bancário não pode ter montante zero.", nameof(amount));
        }

        var normalizedDescription = (description ?? string.Empty).Trim();
        var normalizedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        return new BankStatementLine(Guid.NewGuid(), companyId, accountId, importId, lineKey)
        {
            BookingDate = bookingDate,
            ValueDate = valueDate,
            Description = normalizedDescription.Length <= DescriptionMaxLength ? normalizedDescription : normalizedDescription[..DescriptionMaxLength],
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            Balance = balance is null ? null : decimal.Round(balance.Value, 2, MidpointRounding.AwayFromZero),
            Reference = normalizedReference is { Length: > ReferenceMaxLength } ? normalizedReference[..ReferenceMaxLength] : normalizedReference,
        };
    }

    public void AttachTo(Guid reconciliationId)
    {
        if (reconciliationId == Guid.Empty)
        {
            throw new ArgumentException("A conciliação é obrigatória.", nameof(reconciliationId));
        }

        ReconciliationId = reconciliationId;
    }

    public void Detach() => ReconciliationId = null;

    /// <summary>
    /// A chave de um movimento: data, montante, descrição normalizada e
    /// referência, mais a ordem entre movimentos iguais no mesmo ficheiro (dois
    /// cafés do mesmo valor no mesmo dia são dois movimentos).
    /// </summary>
    public static string KeyOf(DateOnly bookingDate, decimal amount, string? description, string? reference, int occurrence)
    {
        var text = string.Join('|',
            bookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture),
            new string((description ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()),
            (reference ?? string.Empty).Trim().ToUpperInvariant(),
            occurrence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>Um ficheiro de extrato importado, com os saldos que declara.</summary>
public sealed class BankStatementImport
{
    private BankStatementImport()
    {
    }

    public BankStatementImport(
        Guid id,
        Guid companyId,
        Guid accountId,
        string fileName,
        string format,
        string? iban,
        decimal? openingBalance,
        decimal? closingBalance,
        DateOnly? closingDate,
        DateOnly? from,
        DateOnly? to,
        int lineCount)
    {
        Id = id;
        CompanyId = companyId;
        AccountId = accountId;
        FileName = (fileName ?? string.Empty).Trim();
        Format = format;
        Iban = string.IsNullOrWhiteSpace(iban) ? null : iban.Trim().ToUpperInvariant();
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        ClosingDate = closingDate;
        From = from;
        To = to;
        LineCount = lineCount;
        ImportedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid AccountId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    /// <summary><c>camt.053</c> ou <c>CSV</c>.</summary>
    public string Format { get; private set; } = string.Empty;

    public string? Iban { get; private set; }

    public decimal? OpeningBalance { get; private set; }

    public decimal? ClosingBalance { get; private set; }

    public DateOnly? ClosingDate { get; private set; }

    public DateOnly? From { get; private set; }

    public DateOnly? To { get; private set; }

    /// <summary>Movimentos novos gravados com este ficheiro.</summary>
    public int LineCount { get; private set; }

    public DateTimeOffset ImportedAtUtc { get; private set; }
}

/// <summary>
/// Uma conciliação bancária: um ou mais movimentos do extrato de uma conta
/// explicados por um ou mais lançamentos que movimentam essa conta pelo mesmo
/// total — uma transferência que paga várias faturas, os pagamentos com cartão
/// de um dia lançados juntos. Os lançamentos guardam-se pela referência, porque
/// corrigir um lançamento recria-o com a mesma referência.
/// </summary>
public sealed class BankReconciliation
{
    public const int EntryReferenceMaxLength = 48;

    private BankReconciliation()
    {
    }

    public BankReconciliation(Guid id, Guid companyId, Guid accountId, IEnumerable<string> entryReferences, bool automatic)
    {
        if (companyId == Guid.Empty || accountId == Guid.Empty)
        {
            throw new ArgumentException("A empresa e a conta são obrigatórias.");
        }

        var references = (entryReferences ?? [])
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (references.Count == 0)
        {
            throw new ArgumentException("Indique pelo menos um lançamento.", nameof(entryReferences));
        }

        Id = id;
        CompanyId = companyId;
        AccountId = accountId;
        EntryReferences = references;
        Automatic = automatic;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid AccountId { get; private set; }

    public List<string> EntryReferences { get; private set; } = [];

    /// <summary>Se foi proposta pela aplicação (valor e data, ou criada ao lançar do extrato) e não escolhida à mão.</summary>
    public bool Automatic { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
