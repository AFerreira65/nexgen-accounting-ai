using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using NexGenAccountingAI.Application.Chat;
using NexGenAccountingAI.Application.Listings;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>
/// As ferramentas da conversa com a IA (F4F-04), ligadas a uma empresa e a um utilizador. Só leem e fazem-no
/// pelos mesmos serviços do resto da aplicação, por isso valem os mesmos direitos e o mesmo isolamento: o
/// filtro de empresa vem sempre da sessão, nunca do que a IA pede. Os resultados voltam compactos (poucas
/// linhas, com o total de linhas à vista) para a conversa não se encher de dados.
/// </summary>
public sealed class CompanyChatToolHost(
    Guid userId,
    Guid companyId,
    DateOnly today,
    ListingService listings,
    VarianceService variance,
    AnomalyService anomalies,
    ClosingChecklistService closing) : IChatToolHost
{
    /// <summary>O máximo de linhas ou grupos que uma ferramenta devolve à IA.</summary>
    public const int MaxRows = 40;

    /// <summary>O máximo de caracteres de um resultado.</summary>
    public const int MaxResultLength = 14_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string DefinitionHelp = "A definição da listagem (origem, colunas, filtros, ordenação, agrupamento, parâmetros), pelo formato do enunciado.";

    public IReadOnlyList<ChatToolSpec> Tools { get; } =
    [
        new("run_listing",
            "Corre uma listagem sobre a contabilidade da empresa e devolve as primeiras linhas (ou os grupos) e os totais. Serve para extratos, razão, vendas, compras, ativos, bancos, arquivo e saldos por conta e mês.",
            """{"definition": {"type": "object", "description": "{DEF}"}, "parameterValues": {"type": "object", "description": "Os valores dos parâmetros da definição, por nome."}, "groupPath": {"type": "array", "items": {"type": "string"}, "description": "Para abrir um grupo: os valores dos grupos já abertos, do primeiro nível para baixo."}, "take": {"type": "integer", "description": "Quantas linhas (no máximo 40)."}}""".Replace("{DEF}", DefinitionHelp, StringComparison.Ordinal),
            ["definition"]),
        new("pivot",
            "Cruza dois campos de agrupamento (linhas e colunas) e soma uma medida; usa os filtros da definição. Serve para evoluções mensais e comparações.",
            """{"definition": {"type": "object", "description": "{DEF} Só valem a origem, os filtros e os parâmetros."}, "parameterValues": {"type": "object"}, "rowField": {"type": "string"}, "columnField": {"type": "string"}, "measure": {"type": "string", "description": "O campo a somar, ou «contagem»."}}""".Replace("{DEF}", DefinitionHelp, StringComparison.Ordinal),
            ["definition", "rowField", "columnField", "measure"]),
        new("variance",
            "Compara os gastos e os rendimentos de dois períodos, conta a conta, com os terceiros que mais pesaram.",
            """{"from": {"type": "string", "description": "AAAA-MM-DD"}, "to": {"type": "string"}, "compareFrom": {"type": "string"}, "compareTo": {"type": "string"}}""",
            ["from", "to", "compareFrom", "compareTo"]),
        new("anomalies",
            "Procura no período lançamentos que merecem uma segunda vista: duplicados, documentos repetidos, valores fora do habitual, linhas sem terceiro, saldos com o sinal trocado.",
            """{"from": {"type": "string", "description": "AAAA-MM-DD"}, "to": {"type": "string"}}""",
            ["from", "to"]),
        new("closing_status",
            "O papel de trabalho de fecho de um mês: o que falta tratar antes de fechar.",
            """{"year": {"type": "integer"}, "month": {"type": "integer"}}""",
            ["year", "month"]),
    ];

    public async Task<ChatToolOutput> InvokeAsync(string name, string inputJson, CancellationToken cancellationToken = default)
    {
        JsonElement input;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            input = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Error(name, "Os argumentos não são JSON válido.");
        }

        try
        {
            return name switch
            {
                "run_listing" => await RunListingAsync(input, cancellationToken).ConfigureAwait(false),
                "pivot" => await PivotAsync(input, cancellationToken).ConfigureAwait(false),
                "variance" => await VarianceAsync(input, cancellationToken).ConfigureAwait(false),
                "anomalies" => await AnomaliesAsync(input, cancellationToken).ConfigureAwait(false),
                "closing_status" => await ClosingAsync(input, cancellationToken).ConfigureAwait(false),
                _ => Error(name, $"A ferramenta «{name}» não existe."),
            };
        }
        catch (ArgumentException exception)
        {
            // Um pedido mal feito volta à IA como erro, para ela o corrigir.
            return Error(name, exception.Message);
        }
        catch (JsonException)
        {
            return Error(name, "Os argumentos não têm a forma pedida.");
        }
    }

    private async Task<ChatToolOutput> RunListingAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var definition = ReadDefinition(input);
        var path = input.TryGetProperty("groupPath", out var pathNode) && pathNode.ValueKind == JsonValueKind.Array
            ? pathNode.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Null ? null : item.ToString()).ToList()
            : [];
        var take = input.TryGetProperty("take", out var takeNode) && takeNode.TryGetInt32(out var asked) ? Math.Clamp(asked, 1, MaxRows) : MaxRows;
        var result = await listings.RunAsync(userId, companyId, definition, new ListingRequest(ReadValues(input), path, 0, take), cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return Error("run_listing", "Sem acesso à empresa.");
        }

        var payload = new
        {
            tipo = result.Kind == "groups" ? "grupos" : "linhas",
            totalDeLinhas = result.TotalRows,
            colunas = result.Columns.Select(column => column.Label),
            totais = result.Totals,
            grupos = result.Groups.Take(MaxRows).Select(group => new { valor = group.Label, linhas = group.Count, somas = group.Sums }),
            linhas = result.Rows.Take(MaxRows).Select(row => row.Cells),
            maisLinhasQueAsMostradas = result.Kind == "rows" ? result.TotalRows > result.Rows.Count : result.Groups.Count > MaxRows,
        };
        return Ok($"Listagem «{ListingCatalog.Source(definition.Source)?.Label}»: {result.TotalRows} linha(s)", payload);
    }

    private async Task<ChatToolOutput> PivotAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var definition = ReadDefinition(input);
        var pivot = await listings.PivotAsync(
            userId, companyId, definition, ReadValues(input), Text(input, "rowField"), Text(input, "columnField"), Text(input, "measure"), cancellationToken).ConfigureAwait(false);
        if (pivot is null)
        {
            return Error("pivot", "Sem acesso à empresa.");
        }

        if (pivot.Rows.Count > MaxRows || pivot.Columns.Count > 24)
        {
            return Error("pivot", $"A tabela cruzada tem {pivot.Rows.Count} linhas e {pivot.Columns.Count} colunas: acrescente filtros para ficar com no máximo {MaxRows} e 24.");
        }

        var payload = new
        {
            medida = pivot.MeasureLabel,
            colunas = pivot.Columns.Select(column => column.Label),
            linhas = pivot.Rows.Select((row, index) => new { valor = row.Label, valores = pivot.Values[index], total = pivot.RowTotals[index] }),
            totaisPorColuna = pivot.ColumnTotals,
            totalGeral = pivot.GrandTotal,
        };
        return Ok($"Tabela cruzada: {pivot.MeasureLabel}", payload);
    }

    private async Task<ChatToolOutput> VarianceAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var report = await variance.BuildAsync(
            userId, companyId, Date(input, "from"), Date(input, "to"), Date(input, "compareFrom"), Date(input, "compareTo"), cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Error("variance", "Sem acesso à empresa.");
        }

        var payload = new
        {
            totais = report.Totals,
            resultado = report.Totals.Result,
            resultadoAnterior = report.Totals.PreviousResult,
            contas = report.Rows.Take(15).Select(row => new
            {
                conta = row.AccountCode, nome = row.AccountName, tipo = row.Kind == "income" ? "rendimento" : "gasto",
                atual = row.Current, anterior = row.Previous, diferenca = row.Delta, percentagem = row.Percent, favoravel = row.Favorable,
                terceiros = row.TopParties.Select(party => new { nome = party.Name, atual = party.Current, anterior = party.Previous }),
            }),
        };
        return Ok($"Variações de {report.From:dd/MM/yyyy}–{report.To:dd/MM/yyyy} contra {report.CompareFrom:dd/MM/yyyy}–{report.CompareTo:dd/MM/yyyy}", payload);
    }

    private async Task<ChatToolOutput> AnomaliesAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var report = await anomalies.ScanAsync(userId, companyId, Date(input, "from"), Date(input, "to"), includeDismissed: false, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Error("anomalies", "Sem acesso à empresa.");
        }

        var payload = new
        {
            porRegra = report.Rules.Select(rule => new { regra = rule.Label, achados = rule.Count }),
            achados = report.Findings.Take(20).Select(finding => new
            {
                regra = finding.Rule, gravidade = finding.Severity, titulo = finding.Title, detalhe = finding.Detail,
                lancamentos = finding.EntryReferences, valor = finding.Amount,
            }),
            truncado = report.Truncated || report.Findings.Count > 20,
        };
        return Ok($"Anomalias de {report.From:dd/MM/yyyy} a {report.To:dd/MM/yyyy}: {report.Rules.Sum(rule => rule.Count)} achado(s)", payload);
    }

    private async Task<ChatToolOutput> ClosingAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var year = input.TryGetProperty("year", out var yearNode) && yearNode.TryGetInt32(out var y) ? y : today.Year;
        var month = input.TryGetProperty("month", out var monthNode) && monthNode.TryGetInt32(out var m) ? m : today.Month;
        var checklist = await closing.BuildAsync(userId, companyId, year, month, cancellationToken).ConfigureAwait(false);
        if (checklist is null)
        {
            return Error("closing_status", "Sem acesso à empresa.");
        }

        var payload = new
        {
            mes = checklist.PeriodKey,
            prontoParaFechar = checklist.Ready,
            porTratar = checklist.Attention,
            conferenciasPorFazer = checklist.PendingManual,
            itens = checklist.Items.Select(item => new { grupo = item.Group, verificacao = item.Label, estado = item.State, quantos = item.Count, detalhe = item.Detail }),
        };
        return Ok($"Fecho de {checklist.PeriodKey}: {(checklist.Ready ? "pronto" : $"{checklist.Attention} por tratar, {checklist.PendingManual} conferência(s)")}", payload);
    }

    private static ListingDefinition ReadDefinition(JsonElement input)
    {
        if (!input.TryGetProperty("definition", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Falta a «definition» da listagem.");
        }

        return node.Deserialize<ListingDefinition>(ListingJson.Options) ?? throw new ArgumentException("A «definition» não se lê como listagem.");
    }

    private static Dictionary<string, string> ReadValues(JsonElement input)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (input.TryGetProperty("parameterValues", out var node) && node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                values[property.Name] = property.Value.ToString();
            }
        }

        return values;
    }

    private static string Text(JsonElement input, string name) =>
        input.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString()! : string.Empty;

    private static DateOnly Date(JsonElement input, string name) =>
        DateOnly.TryParseExact(Text(input, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"A data «{name}» tem de ser AAAA-MM-DD.");

    private static ChatToolOutput Ok(string summary, object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        return new ChatToolOutput(
            json.Length <= MaxResultLength ? json : json[..MaxResultLength] + "… [resultado cortado: peça menos linhas ou acrescente filtros]",
            IsError: false, summary);
    }

    private static ChatToolOutput Error(string tool, string message) => new(message, IsError: true, $"{tool}: {message}");
}
