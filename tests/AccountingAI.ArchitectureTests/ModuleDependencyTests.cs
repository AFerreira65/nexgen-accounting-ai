using System.Xml.Linq;

namespace AccountingAI.ArchitectureTests;

[TestClass]
public sealed class ModuleDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AccountingAI.Domain"] = [],
            ["AccountingAI.Application"] = ["AccountingAI.Domain"],
            ["AccountingAI.Infrastructure.Ai"] = ["AccountingAI.Application"],
            ["AccountingAI.Infrastructure.Files"] =
                ["AccountingAI.Application", "AccountingAI.Domain"],
            ["AccountingAI.Infrastructure.SqlServer"] =
                ["AccountingAI.Application", "AccountingAI.Domain"],
            ["AccountingAI.Api"] =
                ["AccountingAI.Application", "AccountingAI.Infrastructure.Ai", "AccountingAI.Infrastructure.Files", "AccountingAI.Infrastructure.SqlServer"],
            ["AccountingAI.Worker"] =
                ["AccountingAI.Application", "AccountingAI.Infrastructure.SqlServer"]
        };

    [TestMethod]
    public void NewModulesOnlyReferenceApprovedLayers()
    {
        var repositoryRoot = FindRepositoryRoot();

        foreach (var module in AllowedReferences)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", module.Key, $"{module.Key}.csproj");
            var actualReferences = ReadProjectReferences(projectPath);

            CollectionAssert.AreEquivalent(
                module.Value,
                actualReferences,
                $"Unexpected project dependency in {module.Key}.");
        }
    }

    [TestMethod]
    public void DomainHasNoPackageDependencies()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AccountingAI.Domain",
            "AccountingAI.Domain.csproj");

        var document = XDocument.Load(projectPath);
        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.HasCount(0, packageReferences, "The domain module must remain dependency-free.");
    }

    [TestMethod]
    public void NewPlatformDoesNotReferenceLegacyCoreOrWinForms()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbidden = new[] { "AccountingAI.Core", "AccountingAI.WinForms" };

        foreach (var module in AllowedReferences.Keys)
        {
            var projectPath = Path.Combine(repositoryRoot, "src", module, $"{module}.csproj");
            var actualReferences = ReadProjectReferences(projectPath);

            foreach (var reference in forbidden)
            {
                CollectionAssert.DoesNotContain(
                    actualReferences,
                    reference,
                    $"{module} must not depend on legacy project {reference} during F1-01.");
            }
        }
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Accounting AI.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Accounting AI repository root.");
    }
}
