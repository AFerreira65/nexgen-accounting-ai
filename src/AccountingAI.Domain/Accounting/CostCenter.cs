namespace AccountingAI.Domain.Accounting;

/// <summary>
/// Um centro de custo da empresa: a dimensão analítica que se indica na linha
/// do lançamento para saber a que atividade, obra ou departamento pertence um
/// gasto ou um rendimento, sem desdobrar o plano de contas.
/// </summary>
public sealed class CostCenter
{
    public const int CodeMaxLength = 20;

    public const int NameMaxLength = 120;

    private CostCenter()
    {
    }

    private CostCenter(Guid id, Guid companyId, string code, string name)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static CostCenter Create(Guid companyId, string code, string name)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new CostCenter(Guid.NewGuid(), companyId, NormalizeCode(code), NormalizeName(name));
    }

    public void Rename(string name) => Name = NormalizeName(name);

    public void SetActive(bool isActive) => IsActive = isActive;

    public static string NormalizeCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("O código é obrigatório.", nameof(code));
        }

        if (normalized.Length > CodeMaxLength)
        {
            throw new ArgumentException($"O código não pode ter mais de {CodeMaxLength} caracteres.", nameof(code));
        }

        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.'))
        {
            throw new ArgumentException("O código só pode ter letras, algarismos, pontos e hífenes.", nameof(code));
        }

        return normalized;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("O nome é obrigatório.", nameof(name));
        }

        if (normalized.Length > NameMaxLength)
        {
            throw new ArgumentException($"O nome não pode ter mais de {NameMaxLength} caracteres.", nameof(name));
        }

        return normalized;
    }
}
