using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>Uma conta de email como o ecrã a vê: nunca traz a senha nem o token de renovação.</summary>
public sealed record MailAccountView(
    bool Configured,
    string Provider,
    string Address,
    string? DisplayName,
    string? SmtpHost,
    int? SmtpPort,
    string SmtpSecurity,
    string? ImapHost,
    int? ImapPort,
    string ImapSecurity,
    string ImapFolder,
    string? Username,
    bool HasSecret,
    string? AuthorizedAccount,
    bool DailyDigest,
    bool PollingEnabled,
    DateTimeOffset? LastUsedAtUtc,
    string? LastError);

public sealed record MailAccountInput(
    string Provider,
    string Address,
    string? DisplayName,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpSecurity,
    string? ImapHost,
    int? ImapPort,
    string? ImapSecurity,
    string? ImapFolder,
    string? Username,
    string? Password,
    bool DailyDigest,
    bool PollingEnabled);

/// <summary>Que fornecedores OAuth2 estão prontos (o produtor registou a aplicação deles).</summary>
public sealed record MailOAuthAvailability(bool Microsoft, bool Google);

/// <summary>
/// As contas de email (ADR-0019): a de cada utilizador, para enviar em nome dele, e a caixa de receção do
/// gabinete. Três maneiras de entrar: servidor SMTP/IMAP com senha (ou senha de aplicação, quando há MFA),
/// Microsoft 365 e Gmail/Google Workspace com OAuth2, em que o MFA é feito no login do fornecedor e a
/// aplicação só guarda, cifrado, um token de renovação. Quem pode configurar o quê decide o chamador.
/// </summary>
public sealed class MailAccountService(
    AccountingDbContext context,
    IAtSecretProtector protector,
    IMailSender sender,
    IMailReader reader,
    IMailOAuthClient oauth)
{
    public async Task<MailAccountView> GetAsync(MailAccountPurpose purpose, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var account = await FindAsync(purpose, ownerId, asTracking: false, cancellationToken).ConfigureAwait(false);
        return account is null ? Empty() : View(account);
    }

    public async Task<MailOAuthAvailability> OAuthAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var apps = await context.MailOAuthApps.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return new MailOAuthAvailability(
            apps.Any(item => item.Provider == MailProvider.Microsoft && item.IsConfigured),
            apps.Any(item => item.Provider == MailProvider.Google && item.IsConfigured));
    }

    public async Task<MailAccountView> SaveAsync(MailAccountPurpose purpose, Guid ownerId, MailAccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.TryParse<MailProvider>(input.Provider, true, out var provider) || !Enum.IsDefined(provider))
        {
            throw new ArgumentException("O tipo de conta tem de ser Password, Microsoft ou Google.", nameof(input));
        }

        var account = await FindAsync(purpose, ownerId, asTracking: true, cancellationToken).ConfigureAwait(false);
        var isNew = account is null;
        account ??= new MailAccount(Guid.NewGuid(), purpose, ownerId, provider, input.Address);
        var providerChanged = !isNew && account.Provider != provider;
        account.Configure(provider, input.Address, input.DisplayName);
        if (providerChanged)
        {
            // Uma senha não serve de token de renovação (nem o contrário): obriga a ligar de novo.
            account.ClearSecret();
        }

        switch (provider)
        {
            case MailProvider.Microsoft:
                account.SetServers("smtp.office365.com", 587, MailSecurity.StartTls, "outlook.office365.com", 993, MailSecurity.Ssl, input.ImapFolder, null);
                break;
            case MailProvider.Google:
                account.SetServers("smtp.gmail.com", 465, MailSecurity.Ssl, "imap.gmail.com", 993, MailSecurity.Ssl, input.ImapFolder, null);
                break;
            default:
                account.SetServers(
                    input.SmtpHost, input.SmtpPort, ParseSecurity(input.SmtpSecurity, MailSecurity.StartTls),
                    input.ImapHost, input.ImapPort, ParseSecurity(input.ImapSecurity, MailSecurity.Ssl),
                    input.ImapFolder, input.Username);
                if (!string.IsNullOrEmpty(input.Password))
                {
                    account.SetSecret(protector.Protect(input.Password));
                }

                break;
        }

        account.SetOptions(input.DailyDigest, input.PollingEnabled);
        if (isNew)
        {
            context.MailAccounts.Add(account);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return View(account);
    }

    public async Task<bool> DeleteAsync(MailAccountPurpose purpose, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var account = await FindAsync(purpose, ownerId, asTracking: true, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return false;
        }

        context.MailAccounts.Remove(account);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Guarda o token de renovação que o fornecedor deu depois de o utilizador autorizar (e fazer o MFA).</summary>
    public async Task StoreOAuthAsync(MailAccountPurpose purpose, Guid ownerId, MailProvider provider, MailOAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new InvalidOperationException("O fornecedor não devolveu um token de renovação: tente de novo e aceite todas as permissões pedidas.");
        }

        var account = await FindAsync(purpose, ownerId, asTracking: true, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            var address = tokens.AccountEmail ?? throw new InvalidOperationException("Indique o endereço de email antes de ligar a conta.");
            account = new MailAccount(Guid.NewGuid(), purpose, ownerId, provider, address);
            context.MailAccounts.Add(account);
        }

        if (account.Provider != provider)
        {
            account.Configure(provider, account.Address, account.DisplayName);
        }

        account.SetServers(
            provider == MailProvider.Microsoft ? "smtp.office365.com" : "smtp.gmail.com", provider == MailProvider.Microsoft ? 587 : 465, provider == MailProvider.Microsoft ? MailSecurity.StartTls : MailSecurity.Ssl,
            provider == MailProvider.Microsoft ? "outlook.office365.com" : "imap.gmail.com", 993, MailSecurity.Ssl, account.ImapFolder, null);
        account.SetSecret(protector.Protect(tokens.RefreshToken), tokens.AccountEmail);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Envia um email em nome do utilizador, com a conta que ele configurou.</summary>
    public Task SendAsync(
        Guid userId, IReadOnlyList<string> to, string subject, string body, IReadOnlyList<MailAttachment>? attachments, CancellationToken cancellationToken = default) =>
        SendCoreAsync(MailAccountPurpose.UserSending, userId, "Configure primeiro a sua conta de email (Definições → A minha conta de email).", to, subject, body, attachments, cancellationToken);

    /// <summary>
    /// Envia um email em nome do gabinete, com a conta da caixa de correio do gabinete (por exemplo,
    /// documentos@gabinete.pt). Serve os convites do portal do cliente, que não são de nenhum utilizador.
    /// </summary>
    public Task SendFromFirmAsync(
        Guid firmId, IReadOnlyList<string> to, string subject, string body, IReadOnlyList<MailAttachment>? attachments, CancellationToken cancellationToken = default) =>
        SendCoreAsync(MailAccountPurpose.FirmInbox, firmId, "O gabinete ainda não configurou a caixa de correio (Dados → Caixa de entrada).", to, subject, body, attachments, cancellationToken);

    private async Task SendCoreAsync(
        MailAccountPurpose purpose, Guid ownerId, string missingMessage, IReadOnlyList<string> to, string subject, string body, IReadOnlyList<MailAttachment>? attachments, CancellationToken cancellationToken)
    {
        var account = await FindAsync(purpose, ownerId, asTracking: true, cancellationToken).ConfigureAwait(false);
        if (account is null || !account.HasSecret)
        {
            throw new InvalidOperationException(missingMessage);
        }

        try
        {
            var endpoint = await EndpointAsync(account, smtp: true, cancellationToken).ConfigureAwait(false);
            await sender.SendAsync(endpoint, new OutgoingMail(account.Address, account.DisplayName, to, subject, body, attachments ?? []), cancellationToken).ConfigureAwait(false);
            account.RecordUse(null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            account.RecordUse(exception.Message);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Testa a ligação da caixa do gabinete (IMAP) sem ler nada.</summary>
    public async Task TestInboxAsync(Guid firmId, CancellationToken cancellationToken = default)
    {
        var account = await FindAsync(MailAccountPurpose.FirmInbox, firmId, asTracking: true, cancellationToken).ConfigureAwait(false);
        if (account is null || !account.HasSecret)
        {
            throw new InvalidOperationException("Configure primeiro a caixa de correio do gabinete.");
        }

        try
        {
            await reader.TestAsync(await EndpointAsync(account, smtp: false, cancellationToken).ConfigureAwait(false), account.ImapFolder, cancellationToken).ConfigureAwait(false);
            account.RecordUse(null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            account.RecordUse(exception.Message);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>O servidor e as credenciais prontos a usar; com OAuth2, renova o token de acesso.</summary>
    public async Task<MailEndpoint> EndpointAsync(MailAccount account, bool smtp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var host = smtp ? account.SmtpHost : account.ImapHost;
        var port = smtp ? account.SmtpPort : account.ImapPort;
        if (string.IsNullOrWhiteSpace(host) || port is null)
        {
            throw new InvalidOperationException(smtp ? "Falta o servidor de envio (SMTP) da conta." : "Falta o servidor de receção (IMAP) da conta.");
        }

        var security = smtp ? account.SmtpSecurity : account.ImapSecurity;
        var secret = protector.Unprotect(account.Secret);
        if (account.Provider == MailProvider.Password)
        {
            return new MailEndpoint(host, port.Value, security, account.Username ?? account.Address, Password: secret);
        }

        var app = await context.MailOAuthApps.AsNoTracking().SingleOrDefaultAsync(item => item.Provider == account.Provider, cancellationToken).ConfigureAwait(false);
        if (app is null || !app.IsConfigured)
        {
            throw new InvalidOperationException($"A ligação {(account.Provider == MailProvider.Microsoft ? "à Microsoft" : "à Google")} ainda não está preparada: peça ao produtor da aplicação para registar a aplicação OAuth.");
        }

        var tokens = await oauth.RefreshAsync(account.Provider, new MailOAuthApp(app.ClientId, protector.Unprotect(app.ClientSecret), app.Tenant), secret, cancellationToken).ConfigureAwait(false);
        return new MailEndpoint(host, port.Value, security, account.AuthorizedAccount ?? account.Address, AccessToken: tokens.AccessToken);
    }

    public Task<MailAccount?> FindAccountAsync(MailAccountPurpose purpose, Guid ownerId, CancellationToken cancellationToken = default) =>
        FindAsync(purpose, ownerId, asTracking: true, cancellationToken);

    private Task<MailAccount?> FindAsync(MailAccountPurpose purpose, Guid ownerId, bool asTracking, CancellationToken cancellationToken)
    {
        var query = asTracking ? context.MailAccounts.AsQueryable() : context.MailAccounts.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.Purpose == purpose && item.OwnerId == ownerId, cancellationToken);
    }

    private static MailSecurity ParseSecurity(string? value, MailSecurity fallback) =>
        Enum.TryParse<MailSecurity>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static MailAccountView Empty() => new(false, nameof(MailProvider.Password), string.Empty, null, null, null, nameof(MailSecurity.StartTls), null, null, nameof(MailSecurity.Ssl), "INBOX", null, false, null, false, false, null, null);

    private static MailAccountView View(MailAccount account) => new(
        true, account.Provider.ToString(), account.Address, account.DisplayName, account.SmtpHost, account.SmtpPort, account.SmtpSecurity.ToString(),
        account.ImapHost, account.ImapPort, account.ImapSecurity.ToString(), account.ImapFolder, account.Username, account.HasSecret, account.AuthorizedAccount,
        account.DailyDigest, account.PollingEnabled, account.LastUsedAtUtc, account.LastError);
}
