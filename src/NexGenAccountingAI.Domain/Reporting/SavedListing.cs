namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>Quem vê uma listagem guardada.</summary>
public enum ListingScope
{
    /// <summary>Só quem a criou.</summary>
    Private = 0,

    /// <summary>Quem tem acesso à empresa.</summary>
    Company = 1,

    /// <summary>Todas as empresas do gabinete.</summary>
    Firm = 2,
}

/// <summary>
/// Uma listagem desenhada pelo utilizador (F4D-01): um nome, quem a vê e a definição (origem,
/// colunas, filtros, ordenação, agrupamento e parâmetros) em JSON. Só guarda a definição —
/// os dados lêem-se de cada vez, com os direitos de quem a corre.
/// </summary>
public sealed class SavedListing
{
    public const int NameMaxLength = 120;

    public const int DescriptionMaxLength = 500;

    private SavedListing()
    {
    }

    private SavedListing(Guid id, Guid companyId, Guid firmId, Guid ownerUserId, ListingScope scope, string name, string? description, string definitionJson)
    {
        Id = id;
        CompanyId = companyId;
        FirmId = firmId;
        OwnerUserId = ownerUserId;
        Scope = scope;
        Name = name;
        Description = description;
        DefinitionJson = definitionJson;
    }

    public Guid Id { get; private set; }

    /// <summary>A empresa em que foi criada.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>O gabinete da empresa em que foi criada: é por ele que uma listagem do gabinete chega às outras empresas.</summary>
    public Guid FirmId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public ListingScope Scope { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>A <c>ListingDefinition</c> serializada em JSON.</summary>
    public string DefinitionJson { get; private set; } = "{}";

    /// <summary>Marcada como favorita pelo dono.</summary>
    public bool IsFavorite { get; private set; }

    public uint RowVersion { get; private set; }

    public static SavedListing Create(
        Guid companyId, Guid firmId, Guid ownerUserId, ListingScope scope, string name, string? description, string definitionJson)
    {
        if (companyId == Guid.Empty || firmId == Guid.Empty || ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("A empresa, o gabinete e o utilizador são obrigatórios.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);
        return new SavedListing(Guid.NewGuid(), companyId, firmId, ownerUserId, scope, Trim(name, NameMaxLength), Normalize(description), definitionJson);
    }

    public void Update(ListingScope scope, string name, string? description, string definitionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);
        Scope = scope;
        Name = Trim(name, NameMaxLength);
        Description = Normalize(description);
        DefinitionJson = definitionJson;
    }

    public void SetFavorite(bool value) => IsFavorite = value;

    private static string Trim(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string? Normalize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : Trim(description, DescriptionMaxLength);
}
