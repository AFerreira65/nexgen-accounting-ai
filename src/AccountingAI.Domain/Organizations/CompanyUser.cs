namespace AccountingAI.Domain.Organizations;

public sealed class CompanyUser
{
    private CompanyUser()
    {
    }

    public CompanyUser(Guid companyId, Guid userId, CompanyUserRole role)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        CompanyId = companyId;
        UserId = userId;
        Role = role;
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public CompanyUserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public Company Company { get; private set; } = null!;

    public UserAccount User { get; private set; } = null!;

    public UserCompanySettings? Settings { get; private set; }

    public void ChangeRole(CompanyUserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Role = role;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
