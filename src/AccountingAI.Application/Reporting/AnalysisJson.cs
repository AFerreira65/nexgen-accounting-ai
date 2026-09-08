using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountingAI.Application.Reporting;

/// <summary>
/// Opções de JSON do módulo de análises: <c>camelCase</c> e enums como texto.
/// É local ao módulo para não mexer nas opções globais da API (onde outros
/// enums são serializados como número).
/// </summary>
public static class AnalysisJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(AnalysisDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);

    public static AnalysisDefinition? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AnalysisDefinition>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
