using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataLinq.CLI;

internal static class CliConfigDiscovery
{
    private const string ConfigFileName = "datalinq.json";

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules",
        "artifacts",
        "_site"
    };

    public static string ResolveConfigFilePath(string configPath)
    {
        var fullPath = Path.GetFullPath(configPath);
        return Directory.Exists(fullPath)
            ? Path.Combine(fullPath, ConfigFileName)
            : fullPath;
    }

    public static string ResolveDiscoveryRoot(string configPath)
    {
        var fullPath = Path.GetFullPath(configPath);

        if (Directory.Exists(fullPath))
            return fullPath;

        if (File.Exists(fullPath))
            return Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

        var fileName = Path.GetFileName(fullPath);
        if (fileName.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase) ||
            Path.HasExtension(fullPath))
        {
            return Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        }

        return fullPath;
    }

    public static IReadOnlyList<string> DiscoverConfigFiles(string configPath)
    {
        var root = ResolveDiscoveryRoot(configPath);
        if (!Directory.Exists(root))
            return [];

        var configFiles = new List<string>();
        DiscoverConfigFiles(root, configFiles);

        return configFiles
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void DiscoverConfigFiles(string directory, List<string> configFiles)
    {
        var configPath = Path.Combine(directory, ConfigFileName);
        if (File.Exists(configPath))
            configFiles.Add(Path.GetFullPath(configPath));

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedDirectoryNames.Contains(Path.GetFileName(childDirectory)))
                continue;

            DiscoverConfigFiles(childDirectory, configFiles);
        }
    }
}
