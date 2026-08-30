namespace AccountingAI.Core.Models;

public enum ValidationSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string SourceFile,
    string Area,
    string Message);
