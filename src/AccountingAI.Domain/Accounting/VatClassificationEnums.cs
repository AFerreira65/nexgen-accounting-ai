namespace AccountingAI.Domain.Accounting;

/// <summary>A taxa de IVA que uma conta representa.</summary>
public enum VatRate
{
    /// <summary>Sem classificação de taxa.</summary>
    Nenhuma = 0,

    /// <summary>Operação isenta ou não sujeita.</summary>
    Isento = 1,

    /// <summary>Taxa reduzida.</summary>
    Reduzida = 2,

    /// <summary>Taxa intermédia.</summary>
    Intermedia = 3,

    /// <summary>Taxa normal.</summary>
    Normal = 4,
}

/// <summary>A natureza da operação de IVA de uma conta (lista das mais comuns).</summary>
public enum VatOperationNature
{
    Nenhuma = 0,

    /// <summary>Transmissões de bens no território nacional.</summary>
    TransmissoesInternas = 1,

    /// <summary>Prestações de serviços no território nacional.</summary>
    PrestacoesServicosInternas = 2,

    /// <summary>Exportações e outras operações isentas com direito à dedução.</summary>
    ExportacoesEIsentasComDeducao = 3,

    /// <summary>Aquisições intracomunitárias de bens.</summary>
    AquisicoesIntracomunitariasBens = 4,

    /// <summary>Aquisições intracomunitárias de serviços.</summary>
    AquisicoesIntracomunitariasServicos = 5,

    /// <summary>Outras operações com IVA autoliquidado pelo adquirente.</summary>
    AutoliquidacaoOutras = 6,

    /// <summary>Operações isentas sem direito à dedução.</summary>
    OperacoesIsentasSemDeducao = 7,

    /// <summary>IVA dedutível — imobilizado / ativos fixos.</summary>
    IvaDedutivelImobilizado = 8,

    /// <summary>IVA dedutível — inventários / existências.</summary>
    IvaDedutivelExistencias = 9,

    /// <summary>IVA dedutível — outros bens e serviços.</summary>
    IvaDedutivelOutros = 10,

    /// <summary>Regularizações a favor do sujeito passivo.</summary>
    RegularizacoesFavorSujeitoPassivo = 11,

    /// <summary>Regularizações a favor do Estado.</summary>
    RegularizacoesFavorEstado = 12,
}

/// <summary>A região relevante para os campos regionais da declaração.</summary>
public enum VatRegion
{
    Continente = 0,
    Acores = 1,
    Madeira = 2,
}
