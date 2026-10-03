namespace AccountingAI.Domain.Accounting;

public sealed class JournalEntryLine
{
    private JournalEntryLine()
    {
    }

    internal JournalEntryLine(
        Guid id,
        Guid companyId,
        Guid journalEntryId,
        Guid accountId,
        int sequence,
        string description,
        decimal debit,
        decimal credit,
        Guid? partyId = null)
    {
        Id = id;
        CompanyId = companyId;
        JournalEntryId = journalEntryId;
        AccountId = accountId;
        Sequence = sequence;
        Description = description;
        Debit = debit;
        Credit = credit;
        PartyId = partyId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid JournalEntryId { get; private set; }

    public Guid AccountId { get; private set; }

    public int Sequence { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }

    /// <summary>O terceiro (cliente, fornecedor) a que a linha diz respeito, se houver.</summary>
    public Guid? PartyId { get; private set; }

    public JournalEntry JournalEntry { get; private set; } = null!;

    public LedgerAccount Account { get; private set; } = null!;

    public Party? Party { get; private set; }
}
