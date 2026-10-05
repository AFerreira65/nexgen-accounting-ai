namespace NexGenAccountingAI.Domain.Reporting;

/// <summary>
/// Um mapa desenhado pelo utilizador para uma empresa (F4B-03): um nome e a definição
/// (linhas, colunas e fórmulas do motor de mapas) serializada em JSON. Só guarda a
/// definição — os valores calculam-se de cada vez.
/// </summary>
public sealed class FinancialMap
{
    /// <summary>Máximo de caracteres do nome.</summary>
    public const int NameMaxLength = 120;

    /// <summary>Máximo de caracteres da descrição.</summary>
    public const int DescriptionMaxLength = 500;

    private FinancialMap()
    {
    }

    private FinancialMap(Guid id, Guid companyId, string name, string? description, string definitionJson)
    {
        Id = id;
        CompanyId = companyId;
        Name = name;
        Description = description;
        DefinitionJson = definitionJson;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>A <c>MapDefinition</c> serializada em JSON.</summary>
    public string DefinitionJson { get; private set; } = "{}";

    public uint RowVersion { get; private set; }

    public static FinancialMap Create(Guid companyId, string name, string? description, string definitionJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);

        return new FinancialMap(Guid.NewGuid(), companyId, Trim(name, NameMaxLength), Normalize(description), definitionJson);
    }

    public void Update(string name, string? description, string definitionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);

        Name = Trim(name, NameMaxLength);
        Description = Normalize(description);
        DefinitionJson = definitionJson;
    }

    private static string Trim(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string? Normalize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : Trim(description, DescriptionMaxLength);
}
