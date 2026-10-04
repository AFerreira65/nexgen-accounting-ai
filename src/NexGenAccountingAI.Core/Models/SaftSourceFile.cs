namespace NexGenAccountingAI.Core.Models;

public sealed record SaftSourceFile(
    string FileName,
    string FullPath,
    long Size,
    string Version,
    string TaxAccountingBasis,
    string CompanyId,
    string CompanyName,
    string StartDate,
    string EndDate,
    string DetectedContent);
