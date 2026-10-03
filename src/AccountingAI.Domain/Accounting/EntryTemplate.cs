namespace AccountingAI.Domain.Accounting;

/// <summary>O papel de uma linha num lançamento-tipo.</summary>
public enum EntryTemplateLineKind
{
    /// <summary>A base tributável: recebe o valor indicado (ou o que dele resulta sem IVA).</summary>
    Base = 0,

    /// <summary>O imposto: a base vezes a percentagem do código de IVA da linha.</summary>
    Vat = 1,

    /// <summary>A contrapartida: o valor que equilibra o lançamento (cliente, fornecedor, banco).</summary>
    Balance = 2,
}

public enum EntryTemplateSide
{
    Debit = 0,
    Credit = 1,
}

/// <summary>Uma linha calculada por <see cref="EntryTemplate.Compute"/>.</summary>
public sealed record EntryTemplateAmount(EntryTemplateLine Line, decimal Debit, decimal Credit);

/// <summary>
/// Um lançamento-tipo: o diário, o tipo de documento e as linhas de um
/// lançamento repetitivo (uma compra, uma venda), com uma linha de base, as
/// linhas de IVA que dela se calculam e a contrapartida que equilibra.
/// </summary>
public sealed class EntryTemplate
{
    public const int CodeMaxLength = 12;

    public const int NameMaxLength = 120;

    public const int DescriptionMaxLength = 500;

    private readonly List<EntryTemplateLine> lines = [];

    private EntryTemplate()
    {
    }

    private EntryTemplate(Guid id, Guid companyId, string code)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid JournalId { get; private set; }

    public Guid? DocumentTypeId { get; private set; }

    /// <summary>A descrição proposta para o lançamento; `null` para a deixar ao utilizador.</summary>
    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<EntryTemplateLine> Lines => lines;

    public static EntryTemplate Create(Guid companyId, string code)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        return new EntryTemplate(Guid.NewGuid(), companyId, NormalizeCode(code));
    }

    public void Update(string name, Guid journalId, Guid? documentTypeId, string? description, bool isActive)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("O nome é obrigatório.", nameof(name));
        }

        if (normalizedName.Length > NameMaxLength)
        {
            throw new ArgumentException($"O nome não pode ter mais de {NameMaxLength} caracteres.", nameof(name));
        }

        if (journalId == Guid.Empty)
        {
            throw new ArgumentException("O diário é obrigatório.", nameof(journalId));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > DescriptionMaxLength })
        {
            throw new ArgumentException($"A descrição não pode ter mais de {DescriptionMaxLength} caracteres.", nameof(description));
        }

        Name = normalizedName;
        JournalId = journalId;
        DocumentTypeId = documentTypeId;
        Description = normalizedDescription;
        IsActive = isActive;
    }

    /// <summary>
    /// Substitui as linhas. Um lançamento-tipo tem exatamente uma linha de base e
    /// uma contrapartida; as linhas de IVA precisam de código de IVA.
    /// </summary>
    public void ReplaceLines(IEnumerable<EntryTemplateLine> newLines)
    {
        var list = newLines?.ToList() ?? [];
        if (list.Count(line => line.Kind == EntryTemplateLineKind.Base) != 1)
        {
            throw new ArgumentException("O lançamento-tipo tem de ter exatamente uma linha de base.");
        }

        if (list.Count(line => line.Kind == EntryTemplateLineKind.Balance) != 1)
        {
            throw new ArgumentException("O lançamento-tipo tem de ter exatamente uma linha de contrapartida.");
        }

        if (list.Any(line => line.Kind == EntryTemplateLineKind.Vat && line.VatCodeId is null))
        {
            throw new ArgumentException("Cada linha de IVA tem de ter um código de IVA.");
        }

        lines.Clear();
        var sequence = 0;
        foreach (var line in list)
        {
            line.Attach(Id, ++sequence);
            lines.Add(line);
        }
    }

    /// <summary>
    /// Calcula os valores das linhas a partir de um valor. Sem IVA incluído, o
    /// valor é a base; com IVA incluído, é o total e a base obtém-se dividindo
    /// pela taxa — só possível com uma única linha de IVA do mesmo lado da base.
    /// A contrapartida recebe o que falta para equilibrar. As linhas que ficam a
    /// zero (um código isento) são omitidas.
    /// </summary>
    /// <param name="amount">O valor indicado, positivo.</param>
    /// <param name="amountIncludesVat">Se o valor já inclui o IVA.</param>
    /// <param name="percentages">A percentagem de cada código de IVA usado nas linhas de IVA.</param>
    public IReadOnlyList<EntryTemplateAmount> Compute(
        decimal amount, bool amountIncludesVat, IReadOnlyDictionary<Guid, decimal> percentages)
    {
        ArgumentNullException.ThrowIfNull(percentages);
        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new ArgumentException("Indique um valor positivo.", nameof(amount));
        }

        var ordered = lines.OrderBy(line => line.Sequence).ToList();
        var baseLine = ordered.Single(line => line.Kind == EntryTemplateLineKind.Base);
        var balanceLine = ordered.Single(line => line.Kind == EntryTemplateLineKind.Balance);
        var vatLines = ordered.Where(line => line.Kind == EntryTemplateLineKind.Vat).ToList();

        decimal Percentage(EntryTemplateLine line) =>
            percentages.TryGetValue(line.VatCodeId!.Value, out var percentage)
                ? percentage
                : throw new ArgumentException("Falta a percentagem de um código de IVA do lançamento-tipo.", nameof(percentages));

        var values = new Dictionary<EntryTemplateLine, decimal>();
        if (amountIncludesVat)
        {
            if (vatLines.Count != 1 || vatLines[0].Side != baseLine.Side)
            {
                throw new InvalidOperationException(
                    "Este lançamento-tipo não permite indicar o valor com IVA incluído: indique a base.");
            }

            var baseAmount = decimal.Round(amount / (1 + (Percentage(vatLines[0]) / 100m)), 2, MidpointRounding.AwayFromZero);
            values[baseLine] = baseAmount;
            // O imposto é o que sobra, para o total bater ao cêntimo com o documento.
            values[vatLines[0]] = amount - baseAmount;
        }
        else
        {
            values[baseLine] = amount;
            foreach (var line in vatLines)
            {
                values[line] = decimal.Round(amount * Percentage(line) / 100m, 2, MidpointRounding.AwayFromZero);
            }
        }

        var debit = values.Where(pair => pair.Key.Side == EntryTemplateSide.Debit).Sum(pair => pair.Value);
        var credit = values.Where(pair => pair.Key.Side == EntryTemplateSide.Credit).Sum(pair => pair.Value);
        var missing = balanceLine.Side == EntryTemplateSide.Debit ? credit - debit : debit - credit;
        if (missing <= 0)
        {
            throw new InvalidOperationException(
                "A contrapartida está do lado errado: com estes valores o lançamento não equilibra.");
        }

        values[balanceLine] = missing;

        return ordered
            .Where(line => values[line] != 0)
            .Select(line => line.Side == EntryTemplateSide.Debit
                ? new EntryTemplateAmount(line, values[line], 0m)
                : new EntryTemplateAmount(line, 0m, values[line]))
            .ToList();
    }

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

        if (!normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException("O código só pode ter letras, algarismos e hífenes.", nameof(code));
        }

        return normalized;
    }
}

/// <summary>Uma linha de um <see cref="EntryTemplate"/>.</summary>
public sealed class EntryTemplateLine
{
    public const int DescriptionMaxLength = 300;

    private EntryTemplateLine()
    {
    }

    public EntryTemplateLine(
        EntryTemplateLineKind kind, EntryTemplateSide side, Guid accountId, Guid? vatCodeId = null, string? description = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A conta é obrigatória.", nameof(accountId));
        }

        if (vatCodeId == Guid.Empty)
        {
            throw new ArgumentException("O código de IVA não pode ser vazio.", nameof(vatCodeId));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > DescriptionMaxLength })
        {
            throw new ArgumentException($"A descrição da linha não pode ter mais de {DescriptionMaxLength} caracteres.", nameof(description));
        }

        Id = Guid.NewGuid();
        Kind = kind;
        Side = side;
        AccountId = accountId;
        VatCodeId = vatCodeId;
        Description = normalizedDescription;
    }

    public Guid Id { get; private set; }

    public Guid EntryTemplateId { get; private set; }

    public int Sequence { get; private set; }

    public EntryTemplateLineKind Kind { get; private set; }

    public EntryTemplateSide Side { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? VatCodeId { get; private set; }

    public string? Description { get; private set; }

    internal void Attach(Guid entryTemplateId, int sequence)
    {
        EntryTemplateId = entryTemplateId;
        Sequence = sequence;
    }
}
