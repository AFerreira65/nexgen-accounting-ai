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
        decimal credit)
    {
        Id = id;
        CompanyId = companyId;
        JournalEntryId = journalEntryId;
        AccountId = accountId;
        Sequence = sequence;
        Description = description;
        Debit = debit;
        Credit = credit;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid JournalEntryId { get; private set; }

    public Guid AccountId { get; private set; }

    public int Sequence { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }

    public JournalEntry JournalEntry { get; private set; } = null!;

    public LedgerAccount Account { get; private set; } = null!;
}
