namespace NexGenAccountingAI.Domain.Mail;

/// <summary>Como a conta de email se autentica no servidor.</summary>
public enum MailProvider
{
    /// <summary>Servidor SMTP/IMAP com utilizador e senha (ou senha de aplicação, quando a conta tem MFA).</summary>
    Password = 1,

    /// <summary>Microsoft 365 / Outlook, com OAuth2 (o MFA faz-se no login da Microsoft).</summary>
    Microsoft = 2,

    /// <summary>Gmail / Google Workspace, com OAuth2 (o MFA faz-se no login da Google).</summary>
    Google = 3,
}

public enum MailSecurity
{
    /// <summary>TLS implícito (normalmente as portas 465 e 993).</summary>
    Ssl = 1,

    /// <summary>Começa sem cifra e passa a TLS com STARTTLS (normalmente as portas 587 e 143).</summary>
    StartTls = 2,

    None = 3,
}

public enum MailAccountPurpose
{
    /// <summary>A conta de um utilizador, de onde a aplicação envia emails em nome dele.</summary>
    UserSending = 1,

    /// <summary>A caixa do gabinete, de onde a aplicação lê os documentos recebidos.</summary>
    FirmInbox = 2,
}

/// <summary>
/// Uma conta de email configurada na aplicação: a de um utilizador (envio) ou a caixa de receção do gabinete.
/// A senha (ou o token de renovação OAuth2) guarda-se cifrada; nunca volta ao ecrã.
/// </summary>
public sealed class MailAccount
{
    public const int AddressMaxLength = 254;

    private MailAccount()
    {
    }

    public MailAccount(Guid id, MailAccountPurpose purpose, Guid ownerId, MailProvider provider, string address)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty)
        {
            throw new ArgumentException("Os identificadores são obrigatórios.");
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        Id = id;
        Purpose = purpose;
        OwnerId = ownerId;
        ImapFolder = "INBOX";
        Configure(provider, address, null);
    }

    public Guid Id { get; private set; }

    public MailAccountPurpose Purpose { get; private set; }

    /// <summary>O utilizador (envio) ou o gabinete (caixa de receção) a quem a conta pertence.</summary>
    public Guid OwnerId { get; private set; }

    public MailProvider Provider { get; private set; }

    /// <summary>O endereço de email: o remetente nos envios, a caixa lida na receção.</summary>
    public string Address { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    public string? SmtpHost { get; private set; }

    public int? SmtpPort { get; private set; }

    public MailSecurity SmtpSecurity { get; private set; } = MailSecurity.StartTls;

    public string? ImapHost { get; private set; }

    public int? ImapPort { get; private set; }

    public MailSecurity ImapSecurity { get; private set; } = MailSecurity.Ssl;

    public string ImapFolder { get; private set; } = "INBOX";

    /// <summary>O utilizador de login no servidor; por omissão o próprio endereço.</summary>
    public string? Username { get; private set; }

    /// <summary>A senha (<see cref="MailProvider.Password"/>) ou o token de renovação (OAuth2), cifrado.</summary>
    public string? Secret { get; private set; }

    /// <summary>Com OAuth2, a conta que o utilizador autorizou no fornecedor.</summary>
    public string? AuthorizedAccount { get; private set; }

    /// <summary>Utilizador: recebe por email o resumo diário dos prazos e das tarefas.</summary>
    public bool DailyDigest { get; private set; }

    /// <summary>Caixa do gabinete: a aplicação lê-a de tempos a tempos.</summary>
    public bool PollingEnabled { get; private set; }

    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public bool HasSecret => !string.IsNullOrEmpty(Secret);

    public void Configure(MailProvider provider, string address, string? displayName)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var trimmed = address.Trim();
        if (trimmed.Length > AddressMaxLength || !trimmed.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Indique um endereço de email válido.", nameof(address));
        }

        Provider = provider;
        Address = trimmed;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    public void SetServers(string? smtpHost, int? smtpPort, MailSecurity smtpSecurity, string? imapHost, int? imapPort, MailSecurity imapSecurity, string? imapFolder, string? username)
    {
        static string? Host(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static int? Port(int? value) => value is null ? null : value is >= 1 and <= 65535 ? value : throw new ArgumentOutOfRangeException(nameof(value), "A porta tem de estar entre 1 e 65535.");

        SmtpHost = Host(smtpHost);
        SmtpPort = Port(smtpPort);
        SmtpSecurity = smtpSecurity;
        ImapHost = Host(imapHost);
        ImapPort = Port(imapPort);
        ImapSecurity = imapSecurity;
        ImapFolder = string.IsNullOrWhiteSpace(imapFolder) ? "INBOX" : imapFolder.Trim();
        Username = Host(username);
    }

    /// <summary>Guarda a senha, ou o token de renovação, já cifrada.</summary>
    public void SetSecret(string protectedSecret, string? authorizedAccount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        Secret = protectedSecret;
        AuthorizedAccount = authorizedAccount;
        LastError = null;
    }

    public void ClearSecret()
    {
        Secret = null;
        AuthorizedAccount = null;
    }

    public void SetOptions(bool dailyDigest, bool pollingEnabled)
    {
        DailyDigest = dailyDigest;
        PollingEnabled = pollingEnabled;
    }

    public void RecordUse(string? error)
    {
        LastUsedAtUtc = DateTimeOffset.UtcNow;
        LastError = error is null ? null : (error.Length > 500 ? error[..500] : error);
    }
}

/// <summary>
/// A aplicação registada no Azure (Microsoft) ou na Google Cloud de que o OAuth2 precisa: uma por fornecedor,
/// definida só pela conta do produtor. O segredo guarda-se cifrado.
/// </summary>
public sealed class MailOAuthAppSettings
{
    private MailOAuthAppSettings()
    {
    }

    public MailOAuthAppSettings(MailProvider provider)
    {
        if (provider is not (MailProvider.Microsoft or MailProvider.Google))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), "Só a Microsoft e a Google usam OAuth2.");
        }

        Provider = provider;
    }

    public MailProvider Provider { get; private set; }

    public string ClientId { get; private set; } = string.Empty;

    public string? ClientSecret { get; private set; }

    /// <summary>Microsoft: o diretório (tenant) da aplicação; vazio = «common».</summary>
    public string? Tenant { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsConfigured => ClientId.Length > 0 && !string.IsNullOrEmpty(ClientSecret);

    public void Update(string clientId, string? protectedSecret, string? tenant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ClientId = clientId.Trim();
        if (!string.IsNullOrEmpty(protectedSecret))
        {
            ClientSecret = protectedSecret;
        }

        Tenant = string.IsNullOrWhiteSpace(tenant) ? null : tenant.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
