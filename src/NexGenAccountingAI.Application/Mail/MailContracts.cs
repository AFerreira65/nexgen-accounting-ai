using NexGenAccountingAI.Domain.Mail;

namespace NexGenAccountingAI.Application.Mail;

/// <summary>
/// Um servidor de email e as credenciais já resolvidas: para as contas OAuth, <see cref="AccessToken"/>
/// é um token de acesso válido (renovado a partir do token de renovação); para as outras, <see cref="Password"/>.
/// </summary>
public sealed record MailEndpoint(
    string Host,
    int Port,
    MailSecurity Security,
    string Username,
    string? Password = null,
    string? AccessToken = null);

public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record OutgoingMail(
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> To,
    string Subject,
    string TextBody,
    IReadOnlyList<MailAttachment> Attachments);

public sealed record InboundAttachment(string FileName, string ContentType, byte[] Content);

public sealed record InboundMessage(
    string MessageId,
    string FromAddress,
    string Subject,
    DateTimeOffset Date,
    string TextBody,
    IReadOnlyList<InboundAttachment> Attachments);

/// <summary>Envia um email por SMTP. A implementação usa o MailKit (<c>Infrastructure.Mail</c>).</summary>
public interface IMailSender
{
    Task SendAsync(MailEndpoint endpoint, OutgoingMail message, CancellationToken cancellationToken = default);
}

/// <summary>Lê o correio recebido por IMAP.</summary>
public interface IMailReader
{
    /// <summary>
    /// Entrega ao <paramref name="handler"/> as mensagens por ler da pasta, da mais antiga para a mais nova.
    /// Só marca como lida a mensagem cujo tratamento devolveu <c>true</c>, por isso uma que falhe é lida de novo.
    /// </summary>
    Task<int> ReadUnseenAsync(
        MailEndpoint endpoint,
        string folder,
        int maxMessages,
        Func<InboundMessage, Task<bool>> handler,
        CancellationToken cancellationToken = default);

    /// <summary>Liga-se e autentica-se, sem ler nada: serve para testar a configuração.</summary>
    Task TestAsync(MailEndpoint endpoint, string folder, CancellationToken cancellationToken = default);
}

/// <summary>A aplicação registada no Azure (Microsoft) ou na Google Cloud, de que o OAuth2 precisa. Só o produtor a define.</summary>
public sealed record MailOAuthApp(string ClientId, string ClientSecret, string? Tenant);

public sealed record MailOAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc, string? AccountEmail);

/// <summary>O fluxo OAuth2 (código de autorização) com a Microsoft e a Google, para o SMTP e o IMAP.</summary>
public interface IMailOAuthClient
{
    /// <summary>O endereço da página de login do fornecedor, onde o utilizador autoriza o acesso (e faz o MFA).</summary>
    Uri AuthorizationUrl(MailProvider provider, MailOAuthApp app, string redirectUri, string state, string? loginHint);

    Task<MailOAuthTokens> ExchangeCodeAsync(MailProvider provider, MailOAuthApp app, string redirectUri, string code, CancellationToken cancellationToken = default);

    Task<MailOAuthTokens> RefreshAsync(MailProvider provider, MailOAuthApp app, string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>A ligação ao servidor de email falhou (rede, autenticação, recusa do servidor); a mensagem é para o utilizador.</summary>
public sealed class MailTransportException(string message, Exception? inner = null) : Exception(message, inner);
