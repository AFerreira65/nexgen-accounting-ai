namespace NexGenAccountingAI.Domain.Operations;

/// <summary>
/// Um pedido feito a um webservice da AT e o que ela respondeu: serviço, operação,
/// ambiente, quem pediu, o código e a mensagem da resposta. Serve de prova e de
/// diagnóstico. Não guarda senhas nem o conteúdo enviado. Só se acrescenta.
/// </summary>
public sealed class AtExchange
{
    private AtExchange()
    {
    }

    public AtExchange(
        Guid id, Guid companyId, string service, string operation, bool production, string period,
        DateTimeOffset requestedAtUtc, Guid? requestedBy, long durationMilliseconds,
        bool reached, int? httpStatus, int? resultCode, string message, long? declarationId, int errorCount, int alertCount)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The exchange identifier is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        Id = id;
        CompanyId = companyId;
        Service = service.Trim();
        Operation = operation.Trim();
        Production = production;
        Period = (period ?? string.Empty).Trim();
        RequestedAtUtc = requestedAtUtc;
        RequestedBy = requestedBy;
        DurationMilliseconds = durationMilliseconds;
        Reached = reached;
        HttpStatus = httpStatus;
        ResultCode = resultCode;
        var text = (message ?? string.Empty).Trim();
        Message = text.Length > 1000 ? text[..1000] : text;
        DeclarationId = declarationId;
        ErrorCount = errorCount;
        AlertCount = alertCount;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>O serviço da AT (por exemplo «dpiva»).</summary>
    public string Service { get; private set; } = string.Empty;

    /// <summary>A operação (por exemplo «validarDeclaracao»).</summary>
    public string Operation { get; private set; } = string.Empty;

    /// <summary>Ambiente de produção (<c>true</c>) ou de testes.</summary>
    public bool Production { get; private set; }

    /// <summary>O período a que o pedido respeita (por exemplo «2026-09» ou «2026-03T»).</summary>
    public string Period { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public Guid? RequestedBy { get; private set; }

    public long DurationMilliseconds { get; private set; }

    /// <summary>Se a AT chegou a responder.</summary>
    public bool Reached { get; private set; }

    public int? HttpStatus { get; private set; }

    /// <summary>O código de resposta da AT; zero é sucesso.</summary>
    public int? ResultCode { get; private set; }

    public string Message { get; private set; } = string.Empty;

    /// <summary>O identificador que a AT deu à declaração, numa submissão aceite.</summary>
    public long? DeclarationId { get; private set; }

    public int ErrorCount { get; private set; }

    public int AlertCount { get; private set; }
}
