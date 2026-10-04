namespace NexGenAccountingAI.Application.Classification;

/// <summary>Uma conta do plano que o modelo pode escolher.</summary>
public sealed record ClassificationAccountOption(string Code, string Name);

/// <summary>Um código de IVA que o modelo pode escolher.</summary>
public sealed record ClassificationVatOption(string Code, string Name, decimal Percentage);

/// <summary>Uma fatura do fornecedor, como o e-Fatura a conhece.</summary>
public sealed record ClassificationInvoiceSample(DateOnly Date, string DocumentType, decimal TaxBase, decimal TaxAmount);

/// <summary>
/// O dossiê determinístico de uma classificação: só o que o modelo precisa — o
/// fornecedor (NIF, nome, setor declarado no e-Fatura), algumas faturas dele e as
/// contas e os códigos de IVA da empresa entre os quais tem de escolher. Nenhum
/// valor sai da plataforma além deste.
/// </summary>
public sealed record PurchaseClassificationDossier(
    string SupplierTaxId,
    string SupplierName,
    string? Sector,
    IReadOnlyList<ClassificationInvoiceSample> Invoices,
    IReadOnlyList<ClassificationAccountOption> Accounts,
    IReadOnlyList<ClassificationVatOption> VatCodes);

/// <summary>A resposta do modelo, por validar contra o plano de contas.</summary>
public sealed record PurchaseClassificationAnswer(
    string? AccountCode,
    string? VatCode,
    string Explanation,
    string Model,
    int PromptTokens,
    int OutputTokens);

/// <summary>
/// Propõe a classificação de um fornecedor sem histórico (F3-21B). A
/// implementação real (<c>ClaudePurchaseClassifier</c>) vive em
/// <c>Infrastructure.Ai</c> e é a única fronteira que conhece o SDK (ADR-0006);
/// sem chave, <see cref="IsConfigured"/> é <c>false</c>.
/// </summary>
public interface IPurchaseClassifier
{
    bool IsConfigured { get; }

    Task<PurchaseClassificationAnswer> ClassifyAsync(PurchaseClassificationDossier dossier, CancellationToken cancellationToken = default);
}
