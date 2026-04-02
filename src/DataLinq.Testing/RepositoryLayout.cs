using System;
using System.IO;

namespace DataLinq.Testing;

public static class RepositoryLayout
{
    public static string FindRepositoryRoot(string? startingDirectory = null)
    {
        var current = new DirectoryInfo(startingDirectory ?? AppContext.BaseDirectory);

        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repository root by walking up from '{startingDirectory ?? AppContext.BaseDirectory}'.");
    }
}
