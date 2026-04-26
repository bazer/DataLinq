using System;
using System.IO;

namespace DataLinq.Generators.Tests;

internal static class GeneratorTestPaths
{
    private static readonly Lazy<string> ProjectRootLazy = new(ResolveProjectRoot);

    public static string TestModel(string fileName) =>
        Path.Combine(ProjectRootLazy.Value, "TestModels", fileName);

    private static string ResolveProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var projectPath = Path.Combine(current.FullName, "DataLinq.Generators.Tests.csproj");
            if (File.Exists(projectPath))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the DataLinq.Generators.Tests project root from '{AppContext.BaseDirectory}'.");
    }
}
