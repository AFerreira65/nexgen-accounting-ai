using System.Reflection;

namespace AccountingAI.Application;

public static class ApplicationAssembly
{
    public static Assembly Reference { get; } = typeof(ApplicationAssembly).Assembly;
}
