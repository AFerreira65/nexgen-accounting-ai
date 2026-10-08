using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Mail;

namespace NexGenAccountingAI.Infrastructure.Mail;

/// <summary>Envia por SMTP com o MailKit. Nunca foi exercitado contra um servidor real nesta máquina.</summary>
public sealed class MailKitSender : IMailSender
{
    public async Task SendAsync(MailEndpoint endpoint, OutgoingMail message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(message);
        if (message.To.Count == 0)
        {
            throw new ArgumentException("Indique pelo menos um destinatário.", nameof(message));
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName ?? message.FromAddress, message.FromAddress));
        foreach (var recipient in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(recipient));
        }

        mime.Subject = message.Subject;
        var body = new BodyBuilder { TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, MailKitSecurity.ToOptions(endpoint.Security), cancellationToken).ConfigureAwait(false);
            await MailKitSecurity.AuthenticateAsync(client, endpoint, cancellationToken).ConfigureAwait(false);
            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (MailKitSecurity.IsTransportFailure(exception))
        {
            throw MailKitSecurity.Wrap(endpoint, exception);
        }
    }
}

internal static class MailKitSecurity
{
    /// <summary>Falhas de rede, de protocolo ou de autenticação — não os erros de programação nem o cancelamento.</summary>
    public static bool IsTransportFailure(Exception exception) =>
        exception is not (OperationCanceledException or ArgumentException or InvalidOperationException);

    public static MailTransportException Wrap(MailEndpoint endpoint, Exception exception) =>
        exception is MailTransportException known
            ? known
            : new MailTransportException($"Não foi possível usar o servidor de email {endpoint.Host}:{endpoint.Port}: {exception.Message}", exception);

    public static SecureSocketOptions ToOptions(MailSecurity security) => security switch
    {
        MailSecurity.Ssl => SecureSocketOptions.SslOnConnect,
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None,
    };

    /// <summary>OAuth2 (XOAUTH2) quando há token de acesso; senão utilizador e senha.</summary>
    public static async Task AuthenticateAsync(MailKit.MailService client, MailEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(endpoint.AccessToken))
        {
            await client.AuthenticateAsync(new SaslMechanismOAuth2(endpoint.Username, endpoint.AccessToken), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await client.AuthenticateAsync(endpoint.Username, endpoint.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
    }
}
