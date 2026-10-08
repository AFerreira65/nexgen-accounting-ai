using System.Text.Json;
using NexGenAccountingAI.Application.Classification;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// Pede ao Claude a conta e o código de IVA para as compras de um fornecedor
/// sem histórico, a partir do dossiê (fornecedor, setor, algumas faturas e as
/// opções do plano da empresa). A resposta é um objeto JSON; a validação contra
/// o plano faz-se do lado da aplicação. Sem chave, não chama nada.
/// </summary>
public sealed class ClaudePurchaseClassifier : IPurchaseClassifier
{
    private const int MaxOutputTokens = 1500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AiOptions options;
    private readonly AiClientProvider provider;

    public ClaudePurchaseClassifier(IOptions<AiOptions> options, AiClientProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.provider = provider ?? new AiClientProvider(options);
    }

    private AnthropicClient? client => provider.Client;

    public bool IsConfigured => client is not null;

    public async Task<PurchaseClassificationAnswer> ClassifyAsync(PurchaseClassificationDossier dossier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        if (client is null)
        {
            return new PurchaseClassificationAnswer(null, null, "A classificação com IA não está configurada.", "—", 0, 0);
        }

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = MaxOutputTokens,
            System = new List<TextBlockParam> { new() { Text = PurchaseClassificationPrompts.System } },
            Messages =
            [
                new() { Role = Role.User, Content = PurchaseClassificationPrompts.User(JsonSerializer.Serialize(dossier, JsonOptions)) },
            ],
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = string.Concat(response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
        var (account, vat, explanation) = PurchaseClassificationPrompts.Parse(text);
        return new PurchaseClassificationAnswer(
            account, vat, explanation, options.Model,
            ToInt(response.Usage?.InputTokens), ToInt(response.Usage?.OutputTokens));
    }

    private static int ToInt(long? value) => value is { } number && number is >= int.MinValue and <= int.MaxValue ? (int)number : 0;
}

/// <summary>O enunciado e a leitura da resposta da classificação de compras.</summary>
public static class PurchaseClassificationPrompts
{
    public const string System = """
        És um contabilista certificado português a classificar as compras de um fornecedor no SNC.
        Recebes, em JSON, o fornecedor (NIF, nome, setor declarado no e-Fatura), algumas faturas dele e as
        contas e os códigos de IVA da empresa. Escolhe UMA conta da lista «accounts» para a base das faturas
        e, se houver IVA dedutível, UM código da lista «vatCodes». Não inventes contas nem códigos: se nenhuma
        conta servir, devolve accountCode a null. Responde só com um objeto JSON:
        {"accountCode": "...", "vatCode": "..." ou null, "explicacao": "uma ou duas frases em português europeu, a dizer porquê"}
        """;

    public static string User(string dossierJson) => $"Dossiê do fornecedor:\n{dossierJson}";

    public static (string? AccountCode, string? VatCode, string Explanation) Parse(string? content)
    {
        var start = content?.IndexOf('{') ?? -1;
        var end = content?.LastIndexOf('}') ?? -1;
        if (content is null || start < 0 || end <= start)
        {
            return (null, null, "A resposta da IA não veio em JSON.");
        }

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
            return (Text("accountCode"), Text("vatCode"), Text("explicacao") ?? Text("explanation") ?? string.Empty);
        }
        catch (JsonException)
        {
            return (null, null, "A resposta da IA não veio em JSON válido.");
        }
    }
}

/// <summary>Sem chave de API: a classificação com IA fica desligada.</summary>
public sealed class NullPurchaseClassifier : IPurchaseClassifier
{
    public bool IsConfigured => false;

    public Task<PurchaseClassificationAnswer> ClassifyAsync(PurchaseClassificationDossier dossier, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PurchaseClassificationAnswer(null, null, "A classificação com IA não está configurada.", "—", 0, 0));
}
