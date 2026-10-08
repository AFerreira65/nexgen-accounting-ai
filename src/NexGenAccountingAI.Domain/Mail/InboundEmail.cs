namespace NexGenAccountingAI.Domain.Mail;

public enum InboundEmailStatus
{
    /// <summary>Todos os anexos passaram para o arquivo digital da empresa.</summary>
    Archived = 1,

    /// <summary>A aplicação não soube a que empresa pertence: espera que alguém a indique.</summary>
    NeedsCompany = 2,

    /// <summary>Sem anexos de documentos (ou posto de parte pelo utilizador).</summary>
    Ignored = 3,
}

/// <summary>
/// Um email recebido na caixa do gabinete. A aplicação guarda o essencial (quem enviou, o assunto, os anexos de
/// documentos) e arquiva os anexos na empresa certa; enquanto não souber qual é, guarda os anexos aqui.
/// O identificador da mensagem (<see cref="MessageId"/>) impede de tratar o mesmo email duas vezes.
/// </summary>
public sealed class InboundEmail
{
    private InboundEmail()
    {
    }

    public InboundEmail(Guid id, Guid firmId, string messageId, string fromAddress, string subject, DateTimeOffset receivedAtUtc, string? bodyPreview)
    {
        if (id == Guid.Empty || firmId == Guid.Empty)
        {
            throw new ArgumentException("Os identificadores são obrigatórios.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        Id = id;
        FirmId = firmId;
        MessageId = Cut(messageId, 300)!;
        FromAddress = Cut(fromAddress, 254) ?? string.Empty;
        Subject = Cut(subject, 300) ?? string.Empty;
        ReceivedAtUtc = receivedAtUtc;
        BodyPreview = Cut(bodyPreview?.ReplaceLineEndings(" "), 500);
        Status = InboundEmailStatus.NeedsCompany;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid FirmId { get; private set; }

    public string MessageId { get; private set; } = string.Empty;

    public string FromAddress { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string? BodyPreview { get; private set; }

    public InboundEmailStatus Status { get; private set; }

    /// <summary>A empresa a que os documentos foram atribuídos.</summary>
    public Guid? CompanyId { get; private set; }

    public Guid? HandledBy { get; private set; }

    public DateTimeOffset? HandledAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public List<InboundEmailAttachment> Attachments { get; private set; } = [];

    public void MarkArchived(Guid companyId, Guid? handledBy)
    {
        Status = InboundEmailStatus.Archived;
        CompanyId = companyId;
        HandledBy = handledBy;
        HandledAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkIgnored(Guid? handledBy)
    {
        Status = InboundEmailStatus.Ignored;
        HandledBy = handledBy;
        HandledAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkNeedsCompany() => Status = InboundEmailStatus.NeedsCompany;

    private static string? Cut(string? value, int max) => value is null ? null : (value.Length > max ? value[..max] : value);
}

/// <summary>Um anexo de documento de um email. Os bytes ficam aqui até o documento passar para o arquivo da empresa.</summary>
public sealed class InboundEmailAttachment
{
    private InboundEmailAttachment()
    {
    }

    public InboundEmailAttachment(Guid id, Guid emailId, string fileName, string contentType, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        EmailId = emailId;
        FileName = fileName.Length > 260 ? fileName[..260] : fileName;
        ContentType = contentType;
        SizeBytes = content.LongLength;
        Content = content;
    }

    public Guid Id { get; private set; }

    public Guid EmailId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Os bytes, enquanto o anexo não foi arquivado; depois ficam vazios.</summary>
    public byte[]? Content { get; private set; }

    /// <summary>O documento do arquivo digital em que o anexo se tornou (ou já existia, se era repetido).</summary>
    public Guid? ArchivedAttachmentId { get; private set; }

    public bool IsArchived => ArchivedAttachmentId is not null;

    public void MarkArchived(Guid? archivedAttachmentId)
    {
        ArchivedAttachmentId = archivedAttachmentId ?? Guid.Empty;
        Content = null;
    }
}
