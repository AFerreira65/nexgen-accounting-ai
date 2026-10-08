using System.Security.Cryptography;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record DocumentAttachmentRow(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset UploadedAtUtc,
    string? Notes,
    string? EntryReference,
    string? QrIssuerTaxId,
    string? QrDocumentNo,
    DateOnly? QrDocumentDate,
    decimal? QrGrossTotal,
    decimal? QrTaxTotal,
    bool CanDelete);

public sealed record DocumentAttachmentUpdate(string? EntryReference, string? Notes, string? QrText);

/// <summary>O que a plataforma já conhece de um documento identificado pelo código QR.</summary>
public sealed record InvoiceQrLookup(
    InvoiceQrCode Qr,
    bool IsSale,
    Guid? EFaturaPurchaseId,
    string? EFaturaPurchaseEntryReference,
    Guid? SalesDocumentId,
    IReadOnlyList<string> EntryReferences,
    IReadOnlyList<Guid> AttachmentIds);

/// <summary>
/// O arquivo digital da empresa (F3-20A). Guarda PDF e imagens com o SHA-256 do
/// conteúdo — o mesmo ficheiro não entra duas vezes — e liga cada um a um
/// lançamento pela referência. Lê o código QR das faturas e procura o que já
/// existe na plataforma com o mesmo documento: a fatura do e-Fatura, o
/// documento de venda, os lançamentos e os documentos arquivados. Um documento
/// ligado a um lançamento conferido não se apaga: é a prova desse lançamento.
/// </summary>
public sealed class DocumentArchiveService(AccountingDbContext context)
{
    private const int ListLimit = 500;

    public async Task<DocumentAttachmentRow?> UploadAsync(
        Guid userId, Guid companyId, string fileName, Stream content, string? entryReference, string? qrText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > DocumentAttachment.MaxSizeBytes)
            {
                throw new ArgumentException($"O ficheiro tem mais de {DocumentAttachment.MaxSizeBytes / 1024 / 1024} MB.", nameof(content));
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var duplicate = await context.DocumentAttachments
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Sha256 == hash)
            .Select(item => new { item.FileName, item.JournalEntryReference })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Este ficheiro já está no arquivo ({duplicate.FileName}{(duplicate.JournalEntryReference is null ? string.Empty : $", lançamento {duplicate.JournalEntryReference}")}).");
        }

        var attachment = DocumentAttachment.Create(companyId, fileName, hash, bytes.LongLength, userId);
        await ApplyAsync(companyId, attachment, new DocumentAttachmentUpdate(entryReference, null, qrText), cancellationToken).ConfigureAwait(false);
        context.DocumentAttachments.Add(attachment);
        context.DocumentAttachmentContents.Add(new DocumentAttachmentContent(attachment.Id, bytes));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await RowsAsync(companyId, [attachment], cancellationToken).ConfigureAwait(false)).Single();
    }

    /// <summary>
    /// Arquiva um ficheiro já em memória, sem verificar o acesso (quem chama já decidiu a empresa). Devolve
    /// <c>null</c> se o mesmo conteúdo já estava no arquivo da empresa. Usado pela receção de emails.
    /// </summary>
    public async Task<DocumentAttachmentRow?> ImportAsync(
        Guid companyId, string fileName, byte[] content, Guid? uploadedBy, string? notes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.LongLength > DocumentAttachment.MaxSizeBytes)
        {
            throw new ArgumentException($"O ficheiro tem mais de {DocumentAttachment.MaxSizeBytes / 1024 / 1024} MB.", nameof(content));
        }

        var hash = Convert.ToHexString(SHA256.HashData(content));
        if (await context.DocumentAttachments.AsNoTracking().AnyAsync(item => item.CompanyId == companyId && item.Sha256 == hash, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var attachment = DocumentAttachment.Create(companyId, fileName, hash, content.LongLength, uploadedBy);
        await ApplyAsync(companyId, attachment, new DocumentAttachmentUpdate(null, notes, null), cancellationToken).ConfigureAwait(false);
        context.DocumentAttachments.Add(attachment);
        context.DocumentAttachmentContents.Add(new DocumentAttachmentContent(attachment.Id, content));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await RowsAsync(companyId, [attachment], cancellationToken).ConfigureAwait(false)).Single();
    }

    public async Task<IReadOnlyList<DocumentAttachmentRow>?> ListAsync(
        Guid userId, Guid companyId, string? entryReference, bool unlinkedOnly, string? search, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var query = context.DocumentAttachments.AsNoTracking().Where(item => item.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(entryReference))
        {
            var reference = entryReference.Trim().ToUpperInvariant();
            query = query.Where(item => item.JournalEntryReference == reference);
        }
        else if (unlinkedOnly)
        {
            query = query.Where(item => item.JournalEntryReference == null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = TextSearch.ContainsPattern(search);
            query = query.Where(item =>
                EF.Functions.ILike(item.FileName, pattern) ||
                (item.QrDocumentNo != null && EF.Functions.ILike(item.QrDocumentNo, pattern)) ||
                (item.QrIssuerTaxId != null && EF.Functions.ILike(item.QrIssuerTaxId, pattern)) ||
                (item.Notes != null && EF.Functions.ILike(item.Notes, pattern)));
        }

        var attachments = await query
            .OrderByDescending(item => item.UploadedAtUtc)
            .Take(ListLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await RowsAsync(companyId, attachments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(DocumentAttachmentRow Row, byte[] Content)?> GetContentAsync(
        Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var attachment = await context.DocumentAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (attachment is null)
        {
            return null;
        }

        var content = await context.DocumentAttachmentContents
            .AsNoTracking()
            .Where(item => item.AttachmentId == id)
            .Select(item => item.Content)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return ((await RowsAsync(companyId, [attachment], cancellationToken).ConfigureAwait(false)).Single(), content);
    }

    /// <summary>Liga (ou desliga, com referência vazia) a um lançamento, muda as notas e lê o código QR.</summary>
    public async Task<DocumentAttachmentRow?> UpdateAsync(
        Guid userId, Guid companyId, Guid id, DocumentAttachmentUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var attachment = await context.DocumentAttachments
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (attachment is null)
        {
            return null;
        }

        if (attachment.JournalEntryReference is { } current &&
            !string.Equals(current, (update.EntryReference ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
            await IsReviewedAsync(companyId, current, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"O documento pertence ao lançamento conferido {current}: reabra o lançamento para o mudar.");
        }

        await ApplyAsync(companyId, attachment, update, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await RowsAsync(companyId, [attachment], cancellationToken).ConfigureAwait(false)).Single();
    }

    public async Task<bool?> DeleteAsync(Guid userId, Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var attachment = await context.DocumentAttachments
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (attachment is null)
        {
            return false;
        }

        if (attachment.JournalEntryReference is { } reference && await IsReviewedAsync(companyId, reference, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"O documento é a prova do lançamento conferido {reference} e conserva-se no arquivo.");
        }

        context.DocumentAttachments.Remove(attachment);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Lê o código QR e procura o mesmo documento no que a empresa já tem.</summary>
    public async Task<InvoiceQrLookup?> LookupQrAsync(Guid userId, Guid companyId, string qrText, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(userId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var qr = ParseQr(qrText);
        var companyTaxId = await context.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.TaxRegistrationNumber)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var isSale = Digits(companyTaxId) == Digits(qr.IssuerTaxId);
        var key = DocumentKey(qr.DocumentNo);

        var purchase = isSale
            ? null
            : (await context.EFaturaPurchases
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.SupplierTaxId == qr.IssuerTaxId)
                .Select(item => new { item.Id, item.InvoiceNo, item.JournalEntryReference })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault(item => DocumentKey(item.InvoiceNo) == key);
        var sale = isSale
            ? await context.SalesDocuments
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.InvoiceNo == qr.DocumentNo)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        // Os lançamentos com o mesmo número de documento, perto da data do documento.
        var entries = context.JournalEntries.AsNoTracking()
            .Where(entry => entry.CompanyId == companyId && entry.Status != JournalEntryStatus.Voided);
        if (qr.DocumentDate is { } date)
        {
            var from = date.AddDays(-60);
            var to = date.AddDays(60);
            entries = entries.Where(entry => entry.EntryDate >= from && entry.EntryDate <= to);
        }

        var lastPart = qr.DocumentNo.Split(['/', ' '], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? qr.DocumentNo;
        var pattern = TextSearch.ContainsPattern(lastPart);
        var candidates = await entries
            .Where(entry => EF.Functions.ILike(entry.DocumentReference, pattern))
            .Select(entry => new { entry.Reference, entry.DocumentReference })
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entryReferences = candidates
            .Where(entry => DocumentKey(entry.DocumentReference) == key)
            .Select(entry => entry.Reference)
            .Distinct()
            .ToList();

        var attachments = await context.DocumentAttachments
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.QrIssuerTaxId == qr.IssuerTaxId && item.QrDocumentNo == qr.DocumentNo)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new InvoiceQrLookup(qr, isSale, purchase?.Id, purchase?.JournalEntryReference, sale, entryReferences, attachments);
    }

    private async Task ApplyAsync(Guid companyId, DocumentAttachment attachment, DocumentAttachmentUpdate update, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(update.EntryReference))
        {
            var reference = update.EntryReference.Trim().ToUpperInvariant();
            var exists = await context.JournalEntries
                .AsNoTracking()
                .AnyAsync(entry => entry.CompanyId == companyId && entry.Reference == reference, cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                throw new ArgumentException($"O lançamento {update.EntryReference} não existe.", nameof(update));
            }
        }

        attachment.LinkToEntry(update.EntryReference);
        attachment.SetNotes(update.Notes);
        if (update.QrText is not null)
        {
            attachment.SetQr(update.QrText.Trim().Length == 0 ? null : ParseQr(update.QrText), update.QrText);
        }
    }

    private async Task<List<DocumentAttachmentRow>> RowsAsync(Guid companyId, IReadOnlyCollection<DocumentAttachment> attachments, CancellationToken cancellationToken)
    {
        var references = attachments.Where(item => item.JournalEntryReference is not null).Select(item => item.JournalEntryReference!).Distinct().ToList();
        var reviewed = references.Count == 0
            ? []
            : (await context.JournalEntries
                .AsNoTracking()
                .Where(entry => entry.CompanyId == companyId && references.Contains(entry.Reference) && entry.Status == JournalEntryStatus.Reviewed)
                .Select(entry => entry.Reference)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);

        return attachments
            .Select(item => new DocumentAttachmentRow(
                item.Id, item.FileName, item.ContentType, item.SizeBytes, item.Sha256, item.UploadedAtUtc, item.Notes,
                item.JournalEntryReference, item.QrIssuerTaxId, item.QrDocumentNo, item.QrDocumentDate, item.QrGrossTotal, item.QrTaxTotal,
                item.JournalEntryReference is null || !reviewed.Contains(item.JournalEntryReference)))
            .ToList();
    }

    private Task<bool> IsReviewedAsync(Guid companyId, string reference, CancellationToken cancellationToken) =>
        context.JournalEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.CompanyId == companyId && entry.Reference == reference && entry.Status == JournalEntryStatus.Reviewed, cancellationToken);

    private static InvoiceQrCode ParseQr(string? text)
    {
        try
        {
            return InvoiceQrCode.Parse(text);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(exception.Message, nameof(text), exception);
        }
    }

    private static string DocumentKey(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private Task<bool> HasAccessAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        context.Companies
            .AsNoTracking()
            .AnyAsync(
                company => company.Id == companyId &&
                    company.IsActive &&
                    company.UserMemberships.Any(membership => membership.UserId == userId && membership.IsActive),
                cancellationToken);
}
