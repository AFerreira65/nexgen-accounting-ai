namespace NexGenAccountingAI.Application.Auditing;

/// <summary>
/// O utilizador do pedido em curso, para o carimbo de auditoria dos registos.
/// É <c>scoped</c> e mutável: a fronteira da API preenche-o com o <c>userId</c>
/// da rota. Fica <c>null</c> quando não há utilizador conhecido (o «quando» é
/// gravado à mesma; o «quem» fica por preencher).
/// </summary>
public interface ICurrentUserAccessor
{
    Guid? UserId { get; set; }
}

/// <inheritdoc cref="ICurrentUserAccessor" />
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    public Guid? UserId { get; set; }
}
