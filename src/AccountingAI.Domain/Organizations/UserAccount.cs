namespace AccountingAI.Domain.Organizations;

public sealed class UserAccount
{
    private UserAccount()
    {
    }

    public UserAccount(Guid id, string displayName, string email)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (!normalizedEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("A valid email address is required.", nameof(email));
        }

        Id = id;
        DisplayName = displayName.Trim();
        Email = email.Trim();
        NormalizedEmail = normalizedEmail;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public ICollection<CompanyUser> CompanyMemberships { get; } = [];

    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }

    public void ChangeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToUpperInvariant();
        if (!normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("A valid email address is required.", nameof(email));
        }

        Email = email.Trim();
        NormalizedEmail = normalized;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
