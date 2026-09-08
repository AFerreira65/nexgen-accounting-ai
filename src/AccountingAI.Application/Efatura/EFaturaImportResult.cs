namespace AccountingAI.Application.Efatura;

/// <param name="LineNumber">Linha no CSV.</param>
/// <param name="Reason">Motivo em pt-PT.</param>
public sealed record EFaturaInvalidRow(int LineNumber, string Reason);

/// <summary>Resultado de importar (ou pré-visualizar) um CSV de compras do e-Fatura.</summary>
/// <param name="BatchId">Identificador do lote; nulo numa pré-visualização.</param>
/// <param name="Imported">Faturas criadas.</param>
/// <param name="AlreadyExisted">Faturas que já existiam (chave NIF+nº+data) — não reimportadas.</param>
/// <param name="Invalid">Linhas ignoradas por estarem incompletas.</param>
/// <param name="InvalidRows">Detalhe das linhas ignoradas, até cinquenta.</param>
/// <param name="Warnings">Avisos ao nível do ficheiro (p. ex. 300 registos).</param>
public sealed record EFaturaImportResult(
    Guid? BatchId,
    int Imported,
    int AlreadyExisted,
    int Invalid,
    IReadOnlyList<EFaturaInvalidRow> InvalidRows,
    IReadOnlyList<string> Warnings);
