using System.Globalization;
using System.Text;
using System.Text.Json;
using NexGenAccountingAI.Application.Listings;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// Pede ao Claude que monte uma listagem a partir de um pedido em português (F4D-05). O modelo vê só
/// o catálogo de origens e campos e o pedido; devolve uma definição em JSON, que a aplicação valida
/// contra o catálogo (<see cref="ListingCatalog.Validate"/>). Se a definição tiver problemas, o modelo
/// recebe-os e tenta uma segunda vez. Sem chave, não chama nada.
/// </summary>
public sealed class ClaudeListingDrafter : IListingDrafter
{
    private const int MaxOutputTokens = 4000;

    private readonly AiOptions options;
    private readonly AnthropicClient? client;

    public ClaudeListingDrafter(IOptions<AiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        if (!string.IsNullOrWhiteSpace(this.options.ApiKey))
        {
            client = new AnthropicClient { ApiKey = this.options.ApiKey };
        }
    }

    public bool IsConfigured => client is not null;

    public string Model => options.Model;

    public async Task<ListingDraft> DraftAsync(string request, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        if (client is null)
        {
            return new ListingDraft(null, "A criação de listagens com IA não está configurada.", null, [], "—", 0, 0);
        }

        var messages = new List<MessageParam> { new() { Role = Role.User, Content = ListingDraftPrompts.User(request, today) } };
        var promptTokens = 0;
        var outputTokens = 0;
        ListingDraftPrompts.Parsed parsed = new(null, string.Empty, null, "A resposta da IA não veio em JSON.");
        var problems = (IReadOnlyList<string>)[];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = MaxOutputTokens,
                System = new List<TextBlockParam> { new() { Text = ListingDraftPrompts.System() } },
                Messages = messages,
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            promptTokens += ToInt(response.Usage?.InputTokens);
            outputTokens += ToInt(response.Usage?.OutputTokens);
            var text = string.Concat(response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
            parsed = ListingDraftPrompts.Parse(text);
            problems = parsed.Definition is null ? [parsed.Error ?? "A resposta da IA não tem definição."] : ListingCatalog.Validate(parsed.Definition);
            if (problems.Count == 0)
            {
                break;
            }

            // Segunda tentativa: o modelo vê o que estava mal.
            messages.Add(new() { Role = Role.Assistant, Content = text });
            messages.Add(new() { Role = Role.User, Content = ListingDraftPrompts.Fix(problems) });
        }

        return new ListingDraft(
            parsed.Name, parsed.Explanation, problems.Count == 0 ? parsed.Definition : null, problems, options.Model, promptTokens, outputTokens);
    }

    private static int ToInt(long? value) => value is { } number && number is >= int.MinValue and <= int.MaxValue ? (int)number : 0;
}

/// <summary>O enunciado, o catálogo e a leitura da resposta do redator de listagens.</summary>
public static class ListingDraftPrompts
{
    public sealed record Parsed(string? Name, string Explanation, ListingDefinition? Definition, string? Error);

    private static readonly JsonSerializerOptions CompactJson = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>O catálogo em JSON compacto: o que o modelo pode usar, e mais nada.</summary>
    public static string CatalogJson() => JsonSerializer.Serialize(
        new
        {
            origens = ListingCatalog.Sources.Select(source => new
            {
                chave = source.Key,
                nome = source.Label,
                descricao = source.Description,
                campos = source.Fields.Select(field => new
                {
                    chave = field.Key,
                    nome = field.Label,
                    tipo = field.Type.ToString().ToLowerInvariant(),
                    filtravel = field.Filterable,
                    ordenavel = field.Sortable,
                    agrupavel = field.Groupable,
                    medida = field.Measure,
                    operadores = field.Filterable ? ListingOperators.For(field.Type) : [],
                }),
            }),
            datasRelativas = ListingParameters.DateTokens,
        },
        CompactJson);

    private const string SystemText = """
        És um contabilista certificado português a montar listagens à medida numa aplicação de contabilidade.
        O utilizador descreve em português o que quer ver; tu devolves a definição da listagem. Nunca recebes
        dados da empresa, só o catálogo de origens e campos abaixo, e não podes inventar origens, campos,
        operadores nem datas relativas: usa só o que o catálogo diz.

        Formato da resposta: apenas um objeto JSON, sem texto à volta:
        {"nome": "título curto da listagem", "explicacao": "uma ou duas frases a dizer o que a listagem mostra e que escolhas fizeste",
         "definition": {
           "source": "chave da origem",
           "columns": ["chaves dos campos a mostrar, pela ordem"],
           "filter": {"op": "and" ou "or", "conditions": [{"field": "...", "operator": "...", "value": "...", "value2": "..."}], "groups": [outros grupos com a mesma forma]},
           "sort": [{"field": "...", "descending": false}],
           "groupBy": ["até três campos agrupáveis, do nível mais alto para o mais baixo"],
           "parameters": [{"name": "letrasENumeros", "label": "Pergunta ao utilizador", "type": "date" ou "text" ou "number", "default": "valor ou data relativa"}]
         }}

        Regras:
        - Os operadores de texto são contains, notcontains, eq, ne, starts, empty, notempty; os de números e datas são eq, ne, gt, ge, lt, le, between. «empty» e «notempty» não levam valor; «between» leva value e value2.
        - Um valor pode ser um parâmetro, escrito @nome, desde que o parâmetro exista em «parameters». Para períodos, cria parâmetros de data com valor por omissão relativo (por exemplo inicio-ano e fim-ano) e filtra o campo de data com «between» entre eles.
        - Só agrupas por campos «agrupavel», e só ordenas ou filtras campos «ordenavel» ou «filtravel». As colunas de medida (débito, crédito, valores) somam-se nos grupos.
        - Os lançamentos, documentos e movimentos anulados já ficam de fora; não os filtres.
        - Escolhe a origem mais adequada ao pedido. Se o pedido não se consegue fazer com o catálogo, devolve a melhor aproximação e diz na explicação o que ficou de fora.
        - Escreve em português europeu.

        Exemplo de definição, para veres a forma (vendas do período por cliente):
        {"source":"vendas","columns":["data","documento","contabilizado","liquido","iva","total"],"filter":{"op":"and","conditions":[{"field":"data","operator":"between","value":"@inicio","value2":"@fim"}]},"sort":[{"field":"data","descending":false}],"groupBy":["cliente"],"parameters":[{"name":"inicio","label":"Desde","type":"date","default":"inicio-ano"},{"name":"fim","label":"Até","type":"date","default":"fim-ano"}]}

        Catálogo:
        {CATALOGO}
        """;

    public static string System() => SystemText.Replace("{CATALOGO}", CatalogJson(), StringComparison.Ordinal);

    public static string User(string request, DateOnly today) =>
        $"Hoje é {today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.\nPedido do utilizador: {request.Trim()}";

    public static string Fix(IReadOnlyList<string> problems) =>
        "A definição tem problemas. Corrige-os e devolve de novo só o objeto JSON completo.\n- " + string.Join("\n- ", problems);

    public static Parsed Parse(string? content)
    {
        var start = content?.IndexOf('{') ?? -1;
        var end = content?.LastIndexOf('}') ?? -1;
        if (content is null || start < 0 || end <= start)
        {
            return new Parsed(null, string.Empty, null, "A resposta da IA não veio em JSON.");
        }

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
            ListingDefinition? definition = null;
            if (root.TryGetProperty("definition", out var node) && node.ValueKind == JsonValueKind.Object)
            {
                definition = node.Deserialize<ListingDefinition>(ListingJson.Options);
            }

            return definition is null
                ? new Parsed(Text("nome"), Text("explicacao") ?? string.Empty, null, "A resposta da IA não traz a definição da listagem.")
                : new Parsed(Text("nome"), Text("explicacao") ?? string.Empty, Normalize(definition), null);
        }
        catch (JsonException)
        {
            return new Parsed(null, string.Empty, null, "A resposta da IA não veio em JSON válido.");
        }
    }

    /// <summary>O que falta a uma definição lida de JSON: listas em vez de <c>null</c> onde o modelo as omitiu.</summary>
    private static ListingDefinition Normalize(ListingDefinition definition) => definition with
    {
        Columns = definition.Columns ?? [],
    };
}

/// <summary>Sem chave de API: a criação de listagens com IA fica desligada.</summary>
public sealed class NullListingDrafter : IListingDrafter
{
    public bool IsConfigured => false;

    public string Model => "—";

    public Task<ListingDraft> DraftAsync(string request, DateOnly today, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ListingDraft(null, "A criação de listagens com IA não está configurada.", null, [], "—", 0, 0));
}
