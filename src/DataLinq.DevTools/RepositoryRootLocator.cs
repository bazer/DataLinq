using System;
using System.IO;

namespace DataLinq.DevTools;

public static class RepositoryRootLocator
{
    public static string Find(string? startingDirectory = null)
    {
        var current = new DirectoryInfo(startingDirectory ?? AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repository root by walking up from '{startingDirectory ?? AppContext.BaseDirectory}'.");
    }
}
