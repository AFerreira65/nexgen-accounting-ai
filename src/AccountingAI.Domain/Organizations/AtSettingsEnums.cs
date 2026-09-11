namespace AccountingAI.Domain.Organizations;

/// <summary>A periodicidade da declaração periódica de IVA de uma empresa.</summary>
public enum VatFilingPeriodicity
{
    /// <summary>Regime mensal (artigo 41.º, n.º 1, alínea a) do CIVA).</summary>
    Mensal = 0,

    /// <summary>Regime trimestral (artigo 41.º, n.º 1, alínea b) do CIVA).</summary>
    Trimestral = 1,
}

/// <summary>O ambiente da AT em que as chamadas de uma empresa devem correr.</summary>
public enum AtEnvironment
{
    Testes = 0,
    Producao = 1,
}
