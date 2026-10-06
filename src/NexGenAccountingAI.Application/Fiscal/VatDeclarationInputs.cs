using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexGenAccountingAI.Application.Fiscal;

/// <summary>
/// O que o utilizador preenche à mão na declaração periódica de IVA (F4C-01), por
/// período: o que a contabilidade não apura. Quadro 02 (dentro ou fora do prazo), 04
/// (declaração recapitulativa apresentada), 05 (inexistência de operações), os campos
/// do quadro 06 que a aplicação não apura (14, 15, 18, 19 e o reembolso pedido, 95), o
/// quadro 06-A (campos 97 a 107) e o quadro 20 (NIF do contabilista certificado).
/// </summary>
/// <param name="OperationsAsAcquirer">A pergunta do quadro 06 («efetuou operações desta natureza?»); <c>null</c> deixa a resposta ser «sim» quando há valores no quadro 06-A.</param>
/// <param name="Fields">Os valores dos campos preenchidos à mão, pelo número do campo.</param>
public sealed record VatDeclarationInputs(
    bool WithinDeadline = true,
    bool FiledRecapitulativeStatement = false,
    bool NoOperations = false,
    bool? OperationsAsAcquirer = null,
    IReadOnlyDictionary<int, decimal>? Fields = null,
    string? CertifiedAccountantTaxNumber = null)
{
    /// <summary>Os campos do quadro 06 que a aplicação não apura e aceita à mão.</summary>
    public static IReadOnlyList<int> SettlementManualFields { get; } = [14, 15, 18, 19, 95];

    /// <summary>Os campos do quadro 06-A, pela ordem do impresso (o 106 é a soma e calcula-se).</summary>
    public static IReadOnlyList<int> DevelopmentFields { get; } = [97, 98, 99, 100, 101, 102, 105, 107, 103, 104];

    public static VatDeclarationInputs Empty { get; } = new();

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public decimal Field(int number) => Fields is not null && Fields.TryGetValue(number, out var value) ? value : 0m;

    /// <summary>A soma do quadro 06-A (campo 106).</summary>
    public decimal DevelopmentTotal => DevelopmentFields.Sum(Field);

    /// <summary>A resposta à pergunta do quadro 06: a indicada, ou «sim» se há valores no 06-A.</summary>
    public bool HasOperationsAsAcquirer => OperationsAsAcquirer ?? DevelopmentTotal != 0m;

    /// <summary>Os problemas, em português; vazio quando está tudo certo.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var allowed = SettlementManualFields.Concat(DevelopmentFields).ToHashSet();
        foreach (var (number, value) in Fields ?? new Dictionary<int, decimal>())
        {
            if (!allowed.Contains(number))
            {
                problems.Add($"O campo {number} não se preenche à mão.");
            }
            else if (value < 0m)
            {
                problems.Add($"O campo {number} não pode ser negativo.");
            }
        }

        var accountant = CertifiedAccountantTaxNumber?.Trim();
        if (!string.IsNullOrEmpty(accountant) && (accountant.Length != 9 || !accountant.All(char.IsAsciiDigit)))
        {
            problems.Add("O NIF do contabilista certificado tem de ter nove dígitos.");
        }

        if (NoOperations && (Fields?.Any(item => item.Value != 0m) ?? false))
        {
            problems.Add("Com «inexistência de operações» assinalada não pode haver valores nos quadros 06 e 06-A.");
        }

        return problems;
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static VatDeclarationInputs Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<VatDeclarationInputs>(json, Json) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
