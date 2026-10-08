namespace NexGenAccountingAI.Domain.Portal;

public enum PortalAccountStatus
{
    /// <summary>Convidado por email, ainda sem senha: não consegue entrar.</summary>
    Invited = 1,

    /// <summary>Confirmou a senha: a conta está ativa.</summary>
    Active = 2,

    Disabled = 3,
}

/// <summary>
/// A conta de um cliente no portal (ADR-0020): entra pelo link que recebe por email e fica ativa quando define a
/// senha. É uma conta à parte dos utilizadores do gabinete — não tem acesso a nada da aplicação, só ao que o
/// gabinete publicou para as empresas a que a conta tem acesso (<see cref="ClientPortalAccess"/>).
/// </summary>
public sealed class ClientPortalAccount
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private ClientPortalAccount()
    {
    }

    public ClientPortalAccount(Guid id, string email, string displayName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador é obrigatório.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !trimmed.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Indique um endereço de email válido.", nameof(email));
        }

        Id = id;
        Email = trimmed;
        NormalizedEmail = trimmed.ToUpperInvariant();
        DisplayName = displayName.Trim().Length > 160 ? displayName.Trim()[..160] : displayName.Trim();
        Status = PortalAccountStatus.Invited;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public PortalAccountStatus Status { get; private set; }

    public string? PasswordHash { get; private set; }

    /// <summary>O SHA-256 do token do convite (o token em si só vai no email).</summary>
    public string? InviteTokenHash { get; private set; }

    public DateTimeOffset? InviteExpiresAtUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public List<ClientPortalAccess> Companies { get; private set; } = [];

    public void SetName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim().Length > 160 ? displayName.Trim()[..160] : displayName.Trim();
    }

    /// <summary>Guarda o hash do token de um convite novo; a conta fica «convidada» se ainda não tinha senha.</summary>
    public void Invite(string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        InviteTokenHash = tokenHash;
        InviteExpiresAtUtc = now + InviteLifetime;
        if (PasswordHash is null)
        {
            Status = PortalAccountStatus.Invited;
        }
    }

    /// <summary>Um pedido de nova senha só se atende de 5 em 5 minutos, para ninguém encher a caixa de outra pessoa.</summary>
    public bool CanIssueInvite(DateTimeOffset now) => InviteExpiresAtUtc is not { } expires || expires - InviteLifetime <= now.AddMinutes(-5);

    public bool InviteIsValid(DateTimeOffset now) => InviteTokenHash is not null && InviteExpiresAtUtc is { } expires && expires > now && Status != PortalAccountStatus.Disabled;

    /// <summary>Confirma a senha (já com hash) e ativa a conta; o convite deixa de valer.</summary>
    public void Activate(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        Status = PortalAccountStatus.Active;
        InviteTokenHash = null;
        InviteExpiresAtUtc = null;
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntilUtc is { } until && until > now;

    public void RecordFailure(DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedUntilUtc = now + LockDuration;
            FailedAttempts = 0;
        }
    }

    public void RecordLogin(DateTimeOffset now)
    {
        FailedAttempts = 0;
        LockedUntilUtc = null;
        LastLoginAtUtc = now;
    }

    public void Disable() => Status = PortalAccountStatus.Disabled;

    public void Enable() => Status = PasswordHash is null ? PortalAccountStatus.Invited : PortalAccountStatus.Active;
}

/// <summary>A conta pode ver o portal de uma empresa.</summary>
public sealed class ClientPortalAccess
{
    private ClientPortalAccess()
    {
    }

    public ClientPortalAccess(Guid accountId, Guid companyId, Guid? grantedBy)
    {
        AccountId = accountId;
        CompanyId = companyId;
        GrantedBy = grantedBy;
        GrantedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid AccountId { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid? GrantedBy { get; private set; }

    public DateTimeOffset GrantedAtUtc { get; private set; }

    public ClientPortalAccount Account { get; private set; } = null!;
}

public enum PortalCategory
{
    Documents = 1,
    Reports = 2,
    TrialBalance = 3,
    Management = 4,
}

/// <summary>
/// Um ficheiro que o gabinete publicou no portal de uma empresa: um relatório, um balancete ou outro mapa em PDF ou
/// Excel (uma fotografia do momento em que foi publicado) ou um documento do arquivo digital (<see cref="AttachmentId"/>,
/// que se lê do arquivo em vez de se copiar).
/// </summary>
public sealed class PortalItem
{
    public const int MaxSizeBytes = 25 * 1024 * 1024;

    private PortalItem()
    {
    }

    public PortalItem(Guid id, Guid companyId, PortalCategory category, string title, string fileName, string contentType, byte[]? content, long sizeBytes, Guid? attachmentId, Guid? publishedBy)
    {
        if (id == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("Os identificadores são obrigatórios.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if ((content is null) == (attachmentId is null))
        {
            throw new ArgumentException("Um item tem conteúdo próprio ou aponta para um documento do arquivo, não os dois.");
        }

        if (sizeBytes > MaxSizeBytes)
        {
            throw new ArgumentException($"O ficheiro passa os {MaxSizeBytes / 1024 / 1024} MB.");
        }

        Id = id;
        CompanyId = companyId;
        Category = category;
        Title = title.Trim().Length > 200 ? title.Trim()[..200] : title.Trim();
        FileName = fileName.Length > 260 ? fileName[..260] : fileName;
        ContentType = contentType;
        Content = content;
        SizeBytes = sizeBytes;
        AttachmentId = attachmentId;
        PublishedBy = publishedBy;
        PublishedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public PortalCategory Category { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Os bytes da fotografia; nulo quando o item aponta para um documento do arquivo.</summary>
    public byte[]? Content { get; private set; }

    public Guid? AttachmentId { get; private set; }

    public Guid? PublishedBy { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }
}

/// <summary>
/// Um documento que o cliente enviou pelo portal. O ficheiro vai para o arquivo digital da empresa
/// (<see cref="AttachmentId"/>) e aqui fica o registo de quem o enviou, para o cliente o ver em «Enviados por si».
/// </summary>
public sealed class ClientPortalUpload
{
    private ClientPortalUpload()
    {
    }

    public ClientPortalUpload(Guid id, Guid companyId, Guid accountId, Guid attachmentId, string fileName, long sizeBytes, DateTimeOffset uploadedAtUtc)
    {
        if (id == Guid.Empty || companyId == Guid.Empty || accountId == Guid.Empty || attachmentId == Guid.Empty)
        {
            throw new ArgumentException("Os identificadores são obrigatórios.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Id = id;
        CompanyId = companyId;
        AccountId = accountId;
        AttachmentId = attachmentId;
        FileName = fileName.Length > 260 ? fileName[..260] : fileName;
        SizeBytes = sizeBytes;
        UploadedAtUtc = uploadedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid AttachmentId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }
}
