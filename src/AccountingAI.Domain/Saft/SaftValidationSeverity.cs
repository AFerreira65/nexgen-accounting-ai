namespace AccountingAI.Domain.Saft;

/// <summary>
/// Gravidade de uma ocorrência num relatório de validação de SAF-T. Partilhada
/// pelas camadas de validação (esquema XSD e regras analíticas).
/// </summary>
public enum SaftValidationSeverity
{
    /// <summary>Nota informativa; não indica um problema.</summary>
    Information,

    /// <summary>Aviso: o ficheiro é utilizável mas há algo a rever.</summary>
    Warning,

    /// <summary>Erro: o ficheiro não conforma e não deve entrar sem decisão explícita.</summary>
    Error
}
