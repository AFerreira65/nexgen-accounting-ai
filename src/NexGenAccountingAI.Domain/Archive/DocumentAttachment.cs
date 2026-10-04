namespace NexGenAccountingAI.Domain.Archive;

/// <summary>
/// Um documento no arquivo digital da empresa (PDF ou imagem de uma fatura, de
/// um recibo, de um extrato), opcionalmente ligado a um lançamento pela
/// referência deste. Guarda o SHA-256 do conteúdo, que prova a integridade e
/// evita duplicados, e os dados do código QR da fatura quando foi lido. O
/// conteúdo está em <see cref="DocumentAttachmentContent"/>, à parte, para as
/// listas não o carregarem.
/// </summary>
public sealed class DocumentAttachment
{
    public const int FileNameMaxLength = 260;

    public const int MaxSizeBytes = 15 * 1024 * 1024;

    public const int EntryReferenceMaxLength = 48;

    public const int NotesMaxLength = 500;

    /// <summary>Os tipos aceites: PDF, imagens e XML (uma fatura eletrónica).</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".xml"] = "application/xml",
    };

    private DocumentAttachment()
    {
    }

    private DocumentAttachment(Guid id, Guid companyId)
    {
        Id = id;
        CompanyId = companyId;
        UploadedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>O SHA-256 do conteúdo, em hexadecimal.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public Guid? UploadedBy { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>A referência do lançamento a que o documento pertence.</summary>
    public string? JournalEntryReference { get; private set; }

    public string? QrText { get; private set; }

    public string? QrIssuerTaxId { get; private set; }

    public string? QrDocumentNo { get; private set; }

    public DateOnly? QrDocumentDate { get; private set; }

    public decimal? QrGrossTotal { get; private set; }

    public decimal? QrTaxTotal { get; private set; }

    public static DocumentAttachment Create(Guid companyId, string fileName, string sha256, long sizeBytes, Guid? uploadedBy)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        var name = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (name.Length == 0)
        {
            throw new ArgumentException("O nome do ficheiro é obrigatório.", nameof(fileName));
        }

        if (name.Length > FileNameMaxLength)
        {
            name = name[^FileNameMaxLength..];
        }

        if (!AllowedContentTypes.TryGetValue(Path.GetExtension(name), out var contentType))
        {
            throw new ArgumentException("Só se arquivam PDF, imagens (PNG, JPEG, WebP, TIFF) e XML.", nameof(fileName));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException("O ficheiro está vazio.", nameof(sizeBytes));
        }

        if (sizeBytes > MaxSizeBytes)
        {
            throw new ArgumentException($"O ficheiro tem mais de {MaxSizeBytes / 1024 / 1024} MB.", nameof(sizeBytes));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        return new DocumentAttachment(Guid.NewGuid(), companyId)
        {
            FileName = name,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256.ToUpperInvariant(),
            UploadedBy = uploadedBy,
        };
    }

    public void LinkToEntry(string? entryReference) =>
        JournalEntryReference = string.IsNullOrWhiteSpace(entryReference) ? null : entryReference.Trim().ToUpperInvariant();

    public void SetNotes(string? notes)
    {
        var normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalized is { Length: > NotesMaxLength })
        {
            throw new ArgumentException($"As notas não podem ter mais de {NotesMaxLength} caracteres.", nameof(notes));
        }

        Notes = normalized;
    }

    public void SetQr(InvoiceQrCode? qr, string? text)
    {
        QrText = qr is null ? null : text?.Trim();
        QrIssuerTaxId = qr?.IssuerTaxId;
        QrDocumentNo = qr?.DocumentNo;
        QrDocumentDate = qr?.DocumentDate;
        QrGrossTotal = qr?.GrossTotal;
        QrTaxTotal = qr?.TaxTotal;
    }
}

/// <summary>O conteúdo de um <see cref="DocumentAttachment"/>.</summary>
public sealed class DocumentAttachmentContent
{
    private DocumentAttachmentContent()
    {
    }

    public DocumentAttachmentContent(Guid attachmentId, byte[] content)
    {
        AttachmentId = attachmentId;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public Guid AttachmentId { get; private set; }

    public byte[] Content { get; private set; } = [];
}
