using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexGenAccountingAI.Application.Maps;

/// <summary>A serialização da definição de um mapa — a mesma na base de dados e na API.</summary>
public static class MapJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(MapDefinition definition) => JsonSerializer.Serialize(definition, Options);

    public static MapDefinition? Deserialize(string json)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<MapDefinition>(json, Options);
            return definition is { Rows: not null, Columns: not null } ? definition : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
