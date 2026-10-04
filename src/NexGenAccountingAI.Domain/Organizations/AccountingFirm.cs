namespace NexGenAccountingAI.Domain.Organizations;

public sealed class AccountingFirm
{
    private AccountingFirm()
    {
    }

    public AccountingFirm(Guid id, string name, string slug)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The accounting firm identifier is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        Id = id;
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public ICollection<Company> Companies { get; } = [];
}
