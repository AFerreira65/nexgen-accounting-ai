namespace NexGenAccountingAI.Domain.Organizations;

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

    /// <summary>
    /// Responsável pela empresa dentro do gabinete — a carteira de quem ela é
    /// (F3-28C). Pode haver mais de um; é só informação, não dá acesso.
    /// </summary>
    public bool IsResponsible { get; private set; }

    /// <summary>
    /// O grupo de permissões do utilizador nesta empresa (ADR-0018). Nulo = o predefinido
    /// pela função: «Consulta» para <see cref="CompanyUserRole.Viewer"/>, «Completo» para as outras.
    /// </summary>
    public Guid? PermissionGroupId { get; private set; }

    public PermissionGroup? PermissionGroup { get; private set; }

    public void AssignPermissionGroup(Guid? groupId) => PermissionGroupId = groupId;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

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

    public void Deactivate()
    {
        IsActive = false;
        IsResponsible = false;
    }

    public void SetResponsible(bool isResponsible)
    {
        if (isResponsible && !IsActive)
        {
            throw new InvalidOperationException("Só um utilizador com acesso ativo pode ser responsável pela empresa.");
        }

        IsResponsible = isResponsible;
    }

    public void Reactivate() => IsActive = true;
}
