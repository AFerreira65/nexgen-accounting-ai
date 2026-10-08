using System.Text.RegularExpressions;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record InboundAttachmentView(Guid Id, string FileName, string ContentType, long SizeBytes, bool Archived);

public sealed record InboundEmailView(
    Guid Id,
    Guid FirmId,
    string FromAddress,
    string Subject,
    DateTimeOffset ReceivedAtUtc,
    string? BodyPreview,
    string Status,
    Guid? CompanyId,
    string? CompanyName,
    IReadOnlyList<InboundAttachmentView> Attachments);

public sealed record InboxPollResult(int Read, int Archived, int NeedsCompany, int Ignored);

/// <summary>
/// A receção de documentos por email (F4E-03): lê a caixa do gabinete, guarda cada email uma só vez
/// (pelo identificador da mensagem) e arquiva os anexos de documentos (PDF, imagens, XML) no arquivo da empresa
/// certa. A empresa descobre-se pelo NIF no assunto, no texto ou no nome dos anexos, ou por o remetente já ter
/// enviado antes documentos de uma só empresa; quando não se sabe, o email espera na caixa de entrada da
/// aplicação até alguém indicar a empresa.
/// </summary>
public sealed partial class MailInboxService(
    AccountingDbContext context,
    MailAccountService accounts,
    IMailReader reader,
    DocumentArchiveService archive,
    PermissionService permissions)
{
    private const int MaxPerRun = 25;

    /// <summary>Imagens mais pequenas do que isto são quase sempre logótipos de assinatura, não documentos.</summary>
    private const int MinImageBytes = 15 * 1024;

    [GeneratedRegex(@"(?<!\d)(\d{9})(?!\d)")]
    private static partial Regex NineDigits();

    /// <summary>Lê a caixa de um gabinete e trata o que chegou.</summary>
    public async Task<InboxPollResult> PollAsync(Guid firmId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAccountAsync(MailAccountPurpose.FirmInbox, firmId, cancellationToken).ConfigureAwait(false);
        if (account is null || !account.HasSecret)
        {
            throw new InvalidOperationException("Configure primeiro a caixa de correio do gabinete.");
        }

        var companies = await context.Companies
            .AsNoTracking()
            .Where(item => item.AccountingFirmId == firmId && item.IsActive)
            .Select(item => new CompanyKey(item.Id, item.TaxRegistrationNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int read = 0, archived = 0, needs = 0, ignored = 0;
        try
        {
            var endpoint = await accounts.EndpointAsync(account, smtp: false, cancellationToken).ConfigureAwait(false);
            await reader.ReadUnseenAsync(endpoint, account.ImapFolder, MaxPerRun, async message =>
            {
                read++;
                var status = await HandleAsync(firmId, companies, message, cancellationToken).ConfigureAwait(false);
                switch (status)
                {
                    case InboundEmailStatus.Archived: archived++; break;
                    case InboundEmailStatus.NeedsCompany: needs++; break;
                    default: ignored++; break;
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);
            account.RecordUse(null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            account.RecordUse(exception.Message);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new InboxPollResult(read, archived, needs, ignored);
    }

    /// <summary>Guarda um email recebido e, se souber a empresa, arquiva os anexos. Também serve os testes.</summary>
    public async Task<InboundEmailStatus> HandleAsync(
        Guid firmId, IReadOnlyList<CompanyKey> companies, InboundMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var messageId = message.MessageId.Length > 300 ? message.MessageId[..300] : message.MessageId;
        var existing = await context.InboundEmails.AsNoTracking()
            .Where(item => item.FirmId == firmId && item.MessageId == messageId)
            .Select(item => (InboundEmailStatus?)item.Status)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is { } already)
        {
            return already;
        }

        var email = new InboundEmail(Guid.NewGuid(), firmId, messageId, message.FromAddress, message.Subject, message.Date.ToUniversalTime(), message.TextBody);
        foreach (var attachment in message.Attachments.Where(IsDocument))
        {
            email.Attachments.Add(new InboundEmailAttachment(Guid.NewGuid(), email.Id, attachment.FileName, attachment.ContentType, attachment.Content));
        }

        context.InboundEmails.Add(email);
        if (email.Attachments.Count == 0)
        {
            email.MarkIgnored(null);
        }
        else if (await GuessCompanyAsync(firmId, companies, message, cancellationToken).ConfigureAwait(false) is { } companyId)
        {
            await ArchiveAsync(email, companyId, null, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return email.Status;
    }

    public async Task<IReadOnlyList<InboundEmailView>> ListAsync(Guid userId, string? status, CancellationToken cancellationToken = default)
    {
        var firms = await FirmIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        var query = context.InboundEmails.AsNoTracking().Include(item => item.Attachments).Where(item => firms.Contains(item.FirmId));
        query = (status ?? "pending").ToLowerInvariant() switch
        {
            "all" => query,
            "archived" => query.Where(item => item.Status == InboundEmailStatus.Archived),
            "ignored" => query.Where(item => item.Status == InboundEmailStatus.Ignored),
            _ => query.Where(item => item.Status == InboundEmailStatus.NeedsCompany),
        };
        var rows = await query.OrderByDescending(item => item.ReceivedAtUtc).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        var companyIds = rows.Where(item => item.CompanyId != null).Select(item => item.CompanyId!.Value).Distinct().ToList();
        var names = await context.Companies.AsNoTracking().Where(item => companyIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.LegalName, cancellationToken).ConfigureAwait(false);
        return rows.Select(item => new InboundEmailView(
            item.Id, item.FirmId, item.FromAddress, item.Subject, item.ReceivedAtUtc, item.BodyPreview, item.Status.ToString(), item.CompanyId,
            item.CompanyId is { } id ? names.GetValueOrDefault(id) : null,
            item.Attachments.OrderBy(attachment => attachment.FileName).Select(attachment => new InboundAttachmentView(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.IsArchived)).ToList())).ToList();
    }

    /// <summary>Atribui um email por tratar a uma empresa e arquiva os anexos. Exige poder juntar documentos nessa empresa.</summary>
    public async Task<bool?> AssignAsync(Guid userId, Guid emailId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var email = await LoadForUserAsync(userId, emailId, cancellationToken).ConfigureAwait(false);
        if (email is null)
        {
            return null;
        }

        var inFirm = await context.Companies.AsNoTracking().AnyAsync(item => item.Id == companyId && item.AccountingFirmId == email.FirmId && item.IsActive, cancellationToken).ConfigureAwait(false);
        var effective = await permissions.ResolveAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (!inFirm || !effective.IsMember)
        {
            return null;
        }

        if (!effective.Has("archive.create"))
        {
            throw new OrganizationAccessDeniedException("Não tem permissão para juntar documentos ao arquivo desta empresa.");
        }

        await ArchiveAsync(email, companyId, userId, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Põe um email de parte (não é um documento): descarta os anexos por arquivar.</summary>
    public async Task<bool> IgnoreAsync(Guid userId, Guid emailId, CancellationToken cancellationToken = default)
    {
        var email = await LoadForUserAsync(userId, emailId, cancellationToken).ConfigureAwait(false);
        if (email is null)
        {
            return false;
        }

        foreach (var attachment in email.Attachments.Where(item => !item.IsArchived))
        {
            attachment.MarkArchived(null);
        }

        email.MarkIgnored(userId);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>O conteúdo de um anexo ainda por arquivar, para o utilizador o ver antes de o atribuir.</summary>
    public async Task<(string FileName, string ContentType, byte[] Content)?> AttachmentAsync(Guid userId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var firms = await FirmIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        var row = await context.InboundEmailAttachments.AsNoTracking()
            .Where(item => item.Id == attachmentId && item.Content != null && context.InboundEmails.Any(email => email.Id == item.EmailId && firms.Contains(email.FirmId)))
            .Select(item => new { item.FileName, item.ContentType, item.Content })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : (row.FileName, row.ContentType, row.Content!);
    }

    private async Task ArchiveAsync(InboundEmail email, Guid companyId, Guid? userId, CancellationToken cancellationToken)
    {
        var notes = $"Recebido por email de {email.FromAddress}: {email.Subject}";
        foreach (var attachment in email.Attachments.Where(item => !item.IsArchived && item.Content is not null))
        {
            var row = await archive.ImportAsync(companyId, attachment.FileName, attachment.Content!, userId, notes.Length > DocumentAttachment.NotesMaxLength ? notes[..DocumentAttachment.NotesMaxLength] : notes, cancellationToken).ConfigureAwait(false);
            attachment.MarkArchived(row?.Id);
        }

        email.MarkArchived(companyId, userId);
    }

    private async Task<InboundEmail?> LoadForUserAsync(Guid userId, Guid emailId, CancellationToken cancellationToken)
    {
        var firms = await FirmIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        return await context.InboundEmails.Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.Id == emailId && firms.Contains(item.FirmId), cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<List<Guid>> FirmIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        context.CompanyUsers.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Select(item => item.Company.AccountingFirmId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>A empresa a que o email pertence, ou nulo quando não se sabe (ou há mais de uma possível).</summary>
    private async Task<Guid?> GuessCompanyAsync(Guid firmId, IReadOnlyList<CompanyKey> companies, InboundMessage message, CancellationToken cancellationToken)
    {
        var text = $"{message.Subject} {message.TextBody} {string.Join(' ', message.Attachments.Select(item => item.FileName))}";
        var found = NineDigits().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var byNif = companies.Where(company => found.Contains(Digits(company.TaxRegistrationNumber))).Select(company => company.Id).Distinct().ToList();
        if (byNif.Count == 1)
        {
            return byNif[0];
        }

        if (byNif.Count > 1 || string.IsNullOrWhiteSpace(message.FromAddress))
        {
            return null;
        }

        // O remetente já enviou documentos de uma só empresa antes: provavelmente é a mesma.
        var history = await context.InboundEmails.AsNoTracking()
            .Where(item => item.FirmId == firmId && item.FromAddress == message.FromAddress && item.CompanyId != null && item.Status == InboundEmailStatus.Archived)
            .Select(item => item.CompanyId!.Value)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return history.Count == 1 ? history[0] : null;
    }

    private static bool IsDocument(InboundAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        if (!DocumentAttachment.AllowedContentTypes.ContainsKey(extension) || attachment.Content.Length == 0 || attachment.Content.LongLength > DocumentAttachment.MaxSizeBytes)
        {
            return false;
        }

        var isImage = !extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        return !isImage || attachment.Content.Length >= MinImageBytes;
    }

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());

    public sealed record CompanyKey(Guid Id, string TaxRegistrationNumber);
}
