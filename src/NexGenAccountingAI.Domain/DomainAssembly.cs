using System.Reflection;

namespace NexGenAccountingAI.Domain;

public static class DomainAssembly
{
    public static Assembly Reference { get; } = typeof(DomainAssembly).Assembly;
}
