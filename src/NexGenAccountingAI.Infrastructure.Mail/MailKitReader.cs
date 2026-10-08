using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Mail;

namespace NexGenAccountingAI.Infrastructure.Mail;

/// <summary>Lê o correio por IMAP com o MailKit. Nunca foi exercitado contra um servidor real nesta máquina.</summary>
public sealed class MailKitReader : IMailReader
{
    public async Task<int> ReadUnseenAsync(
        MailEndpoint endpoint, string folder, int maxMessages, Func<InboundMessage, Task<bool>> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(handler);

        try
        {
            return await ReadAsync(endpoint, folder, maxMessages, handler, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (MailKitSecurity.IsTransportFailure(exception))
        {
            throw MailKitSecurity.Wrap(endpoint, exception);
        }
    }

    private static async Task<int> ReadAsync(
        MailEndpoint endpoint, string folder, int maxMessages, Func<InboundMessage, Task<bool>> handler, CancellationToken cancellationToken)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, MailKitSecurity.ToOptions(endpoint.Security), cancellationToken).ConfigureAwait(false);
        await MailKitSecurity.AuthenticateAsync(client, endpoint, cancellationToken).ConfigureAwait(false);

        var box = string.IsNullOrWhiteSpace(folder) ? client.Inbox : await client.GetFolderAsync(folder, cancellationToken).ConfigureAwait(false);
        await box.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
        var uids = await box.SearchAsync(SearchQuery.NotSeen, cancellationToken).ConfigureAwait(false);

        var handled = 0;
        foreach (var uid in uids.OrderBy(item => item.Id).Take(Math.Max(1, maxMessages)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = await box.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);
            if (await handler(Convert(message)).ConfigureAwait(false))
            {
                await box.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken).ConfigureAwait(false);
                handled++;
            }
        }

        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        return handled;
    }

    public async Task TestAsync(MailEndpoint endpoint, string folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, MailKitSecurity.ToOptions(endpoint.Security), cancellationToken).ConfigureAwait(false);
            await MailKitSecurity.AuthenticateAsync(client, endpoint, cancellationToken).ConfigureAwait(false);
            var box = string.IsNullOrWhiteSpace(folder) ? client.Inbox : await client.GetFolderAsync(folder, cancellationToken).ConfigureAwait(false);
            await box.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (MailKitSecurity.IsTransportFailure(exception))
        {
            throw MailKitSecurity.Wrap(endpoint, exception);
        }
    }

    internal static InboundMessage Convert(MimeMessage message)
    {
        var attachments = new List<InboundAttachment>();
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            var name = part.FileName ?? part.ContentType.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            using var stream = new MemoryStream();
            part.Content?.DecodeTo(stream);
            attachments.Add(new InboundAttachment(name, part.ContentType.MimeType, stream.ToArray()));
        }

        var id = message.MessageId;
        if (string.IsNullOrWhiteSpace(id))
        {
            // Sem Message-ID, uma impressão digital do que identifica a mensagem evita lê-la duas vezes.
            id = "sem-id-" + System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{message.From}|{message.Subject}|{message.Date:o}|{attachments.Count}")))[..32];
        }

        return new InboundMessage(
            id.Trim('<', '>'),
            message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
            message.Subject ?? string.Empty,
            message.Date,
            message.TextBody ?? string.Empty,
            attachments);
    }
}
