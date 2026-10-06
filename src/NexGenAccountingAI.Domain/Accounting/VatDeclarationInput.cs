namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>
/// O que o utilizador preencheu à mão na declaração periódica de IVA de um período
/// (F4C-01): prazo, inexistência de operações, campos 14, 15, 18, 19 e 95, quadro 06-A
/// e NIF do contabilista. Um por <c>(empresa, período)</c>; o conteúdo é a
/// <c>VatDeclarationInputs</c> serializada em JSON.
/// </summary>
public sealed class VatDeclarationInput
{
    private VatDeclarationInput()
    {
    }

    private VatDeclarationInput(Guid id, Guid companyId, DateOnly periodFrom, DateOnly periodTo, string inputsJson)
    {
        Id = id;
        CompanyId = companyId;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        InputsJson = inputsJson;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    /// <summary>A <c>VatDeclarationInputs</c> serializada em JSON.</summary>
    public string InputsJson { get; private set; } = "{}";

    public uint RowVersion { get; private set; }

    public static VatDeclarationInput Create(Guid companyId, DateOnly periodFrom, DateOnly periodTo, string inputsJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        if (periodTo < periodFrom)
        {
            throw new ArgumentException("A data final não pode ser anterior à data inicial.", nameof(periodTo));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(inputsJson);
        return new VatDeclarationInput(Guid.NewGuid(), companyId, periodFrom, periodTo, inputsJson);
    }

    public void Update(string inputsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputsJson);
        InputsJson = inputsJson;
    }
}
