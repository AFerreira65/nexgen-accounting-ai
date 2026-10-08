using NexGenAccountingAI.Domain.Reporting;

namespace NexGenAccountingAI.Application.Ai;

/// <summary>
/// De onde vem a chave da API da Anthropic, para toda a aplicação. A configuração geral, guardada cifrada na base de dados
/// pelo produtor, vale para todas as empresas; a implementação fica em <c>Infrastructure.Persistence</c>.
/// </summary>
public interface IAiApiKeySource
{
    /// <summary>A chave guardada na configuração geral, em claro (só em memória), ou <c>null</c> se não há.</summary>
    string? CurrentKey();

    /// <summary>O modelo escolhido pelo produtor para a função, ou <c>null</c> se usa o de origem.</summary>
    string? ModelOverride(AiFeature feature);
}

/// <summary>Faz a chave guardada valer logo, sem esperar pelo fim da memória temporária.</summary>
public interface IAiApiKeyInvalidator
{
    void Invalidate();
}
