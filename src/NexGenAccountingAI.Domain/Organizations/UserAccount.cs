namespace NexGenAccountingAI.Domain.Organizations;

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

    /// <summary>Telemóvel, telefone e extensão da ficha do utilizador; todos opcionais.</summary>
    public string? Mobile { get; private set; }

    public string? Phone { get; private set; }

    public string? Extension { get; private set; }

    /// <summary>
    /// Contabilista certificado (TOC): tem todas as permissões e é o único que vê e usa a senha
    /// da AT nos envios que a exigem (IES, Modelo 22). Ver ADR-0018.
    /// </summary>
    public bool IsAccountant { get; private set; }

    public void SetAccountant(bool isAccountant) => IsAccountant = isAccountant;

    /// <summary>
    /// Conta do produtor do software: a única que vê a configuração geral da aplicação
    /// (certificado e chave da AT). É uma conta à parte, sem empresas, criada só pela
    /// ferramenta de linha de comandos no servidor — nunca por um ecrã.
    /// </summary>
    public bool IsProducer { get; private set; }

    /// <summary>
    /// O hash PBKDF2 da senha (<see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>).
    /// <c>null</c> enquanto ninguém definir uma senha — sem hash, a conta não
    /// consegue autenticar-se; nunca há uma senha por omissão.
    /// </summary>
    public string? PasswordHash { get; private set; }

    public void MarkAsProducer() => IsProducer = true;

    public void ClearProducer() => IsProducer = false;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

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

    public void SetContacts(string? mobile, string? phone, string? extension)
    {
        Mobile = Clean(mobile, 32, nameof(mobile));
        Phone = Clean(phone, 32, nameof(phone));
        Extension = Clean(extension, 16, nameof(extension));
    }

    private static string? Clean(string? value, int maxLength, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"O contacto excede {maxLength} caracteres.", name);
        }

        return trimmed;
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
