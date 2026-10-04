namespace NexGenAccountingAI.Domain.Accounting;

/// <summary>De onde veio um lançamento contabilístico.</summary>
public enum EntrySource
{
    /// <summary>Lançado à mão na plataforma.</summary>
    Manual = 0,

    /// <summary>Importado dos movimentos de um ficheiro SAF-T.</summary>
    SaftImport = 1,

    /// <summary>Gerado pela abertura do exercício (ou pelos saldos iniciais de um SAF-T).</summary>
    Opening = 2,

    /// <summary>Gerado pelo apuramento de resultados do exercício.</summary>
    Closing = 3,

    /// <summary>Gerado a partir de uma fatura de compra do e-Fatura (lançamento sugerido).</summary>
    EFatura = 4,

    /// <summary>Gerado a partir de um documento de venda de um SAF-T de faturação (lançamento sugerido).</summary>
    SaftBilling = 5,
}
