namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// Um grupo de permissões do gabinete (ADR-0018): um nome e um conjunto de chaves do
/// <see cref="PermissionCatalog"/>. Atribui-se a cada utilizador em cada empresa.
/// Os grupos de origem (<see cref="SystemKey"/>) não se editam.
/// </summary>
public sealed class PermissionGroup
{
    private PermissionGroup()
    {
    }

    public PermissionGroup(Guid id, Guid firmId, string name, string? description, IEnumerable<string> permissions, string? systemKey = null)
    {
        if (id == Guid.Empty || firmId == Guid.Empty)
        {
            throw new ArgumentException("The identifiers are required.");
        }

        Id = id;
        FirmId = firmId;
        SystemKey = systemKey;
        Update(name, description, permissions);
    }

    public Guid Id { get; private set; }

    public Guid FirmId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Preenchido nos grupos de origem; nulo nos grupos criados pelo gabinete.</summary>
    public string? SystemKey { get; private set; }

    public bool IsSystem => SystemKey is not null;

    public List<string> Permissions { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public AccountingFirm Firm { get; private set; } = null!;

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissions);

        var distinct = permissions.Distinct(StringComparer.Ordinal).ToList();
        var unknown = distinct.FirstOrDefault(key => !PermissionCatalog.Exists(key));
        if (unknown is not null)
        {
            throw new ArgumentException($"A permissão «{unknown}» não existe.", nameof(permissions));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 80)
        {
            throw new ArgumentException("O nome do grupo excede 80 caracteres.", nameof(name));
        }

        Name = trimmed;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Permissions = distinct.OrderBy(key => key, StringComparer.Ordinal).ToList();
    }
}
