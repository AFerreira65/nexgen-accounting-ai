namespace AccountingAI.Domain.Accounting;

/// <summary>
/// Contador persistido de numeração dos lançamentos, por empresa, série
/// (código do tipo de documento ou do diário) e exercício. Garante uma
/// sequência sem saltos e independente de contagens sobre os lançamentos.
/// </summary>
public sealed class DocumentNumberSeries
{
    private DocumentNumberSeries()
    {
    }

    public const int DefaultWidth = 5;
    public const int MinWidth = 1;
    public const int MaxWidth = 12;

    public DocumentNumberSeries(Guid id, Guid companyId, string series, int year, int nextNumber = 1, int width = DefaultWidth)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The series identifier is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company identifier is required.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(series);

        if (series.Trim().Length > 24)
        {
            throw new ArgumentException("The series cannot exceed 24 characters.", nameof(series));
        }

        if (year < 2000 || year > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "The fiscal year is out of range.");
        }

        if (nextNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nextNumber), nextNumber, "The next number must be positive.");
        }

        if (width is < MinWidth or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, $"The width must be between {MinWidth} and {MaxWidth}.");
        }

        Id = id;
        CompanyId = companyId;
        Series = series.Trim().ToUpperInvariant();
        Year = year;
        NextNumber = nextNumber;
        Width = width;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Series { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public int NextNumber { get; private set; }

    /// <summary>Largura de preenchimento do número na referência (`{SERIE}-{ANO}-{N}`).</summary>
    public int Width { get; private set; } = DefaultWidth;

    public uint RowVersion { get; private set; }

    /// <summary>Formata a referência de um número desta série e exercício.</summary>
    public string Format(int number) => $"{Series}-{Year}-{number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(Width, '0')}";

    public void ChangeWidth(int width)
    {
        if (width is < MinWidth or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, $"The width must be between {MinWidth} and {MaxWidth}.");
        }

        Width = width;
    }

    /// <summary>Devolve o número corrente e avança o contador.</summary>
    public int Take()
    {
        var current = NextNumber;
        NextNumber = current + 1;
        return current;
    }

    /// <summary>
    /// Avança o próximo número reservado. Um contador nunca pode recuar, para
    /// impedir a reutilização acidental de referências já emitidas.
    /// </summary>
    public void AdvanceTo(int nextNumber)
    {
        if (nextNumber < NextNumber)
        {
            throw new InvalidOperationException("O próximo número não pode ser inferior ao contador atual.");
        }

        NextNumber = nextNumber;
    }
}
