using System.Reflection;

namespace NexGenAccountingAI.Application;

public static class ApplicationAssembly
{
    public static Assembly Reference { get; } = typeof(ApplicationAssembly).Assembly;
}
