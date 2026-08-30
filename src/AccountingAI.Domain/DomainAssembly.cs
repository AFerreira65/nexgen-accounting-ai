using System.Reflection;

namespace AccountingAI.Domain;

public static class DomainAssembly
{
    public static Assembly Reference { get; } = typeof(DomainAssembly).Assembly;
}
