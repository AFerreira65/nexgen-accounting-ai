using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Domain.Reporting;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// O cliente da API da Anthropic partilhado por toda a IA da aplicação. A chave vem, por esta ordem, da configuração geral
/// guardada pelo produtor na base de dados (vale para todas as empresas), da secção <c>Ai</c> do ficheiro de configuração
/// ou da variável <c>ANTHROPIC_API_KEY</c>. Quando a chave muda, o cliente refaz-se sozinho.
/// </summary>
public sealed class AiClientProvider(IOptions<AiOptions> options, IAiApiKeySource? keySource = null)
{
    private readonly object gate = new();
    private string? key;
    private AnthropicClient? client;

    /// <summary>De onde vem a chave em uso: <c>geral</c>, <c>ficheiro</c> ou <c>nenhuma</c>.</summary>
    public string Source => Clean(keySource?.CurrentKey()) is not null ? "geral" : Clean(options.Value.ApiKey) is not null ? "ficheiro" : "nenhuma";

    public AnthropicClient? Client
    {
        get
        {
            var current = Clean(keySource?.CurrentKey()) ?? Clean(options.Value.ApiKey);
            lock (gate)
            {
                if (!string.Equals(current, key, StringComparison.Ordinal))
                {
                    key = current;
                    client = current is null ? null : new AnthropicClient { ApiKey = current };
                }

                return client;
            }
        }
    }

    /// <summary>
    /// O modelo de uma função: o que o produtor escolheu na configuração geral, senão o do ficheiro de configuração, senão o de origem.
    /// </summary>
    public string ModelFor(AiFeature feature) =>
        Clean(keySource?.ModelOverride(feature)) ?? Clean(options.Value.Model) ?? AiModels.Default(feature);

    /// <summary>Um pedido mínimo, para confirmar que a chave em uso é aceite. <c>null</c> se sim; senão, o motivo.</summary>
    public async Task<string?> TestAsync(CancellationToken cancellationToken = default)
    {
        var current = Client;
        if (current is null)
        {
            return "Não há nenhuma chave configurada.";
        }

        try
        {
            await current.Messages.Create(new MessageCreateParams
            {
                Model = "claude-haiku-4-5-20251001",
                MaxTokens = 8,
                Messages = [new() { Role = Role.User, Content = "Responde só: ok" }],
            }, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception.Message.Length > 300 ? exception.Message[..300] : exception.Message;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
