using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DataLinq.Generators.Tests;

internal static class GeneratorMetadataReferenceCache
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly Lazy<ImmutableArray<string>> BaseLocations =
        new(CreateBaseLocations, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, Lazy<ImmutableArray<MetadataReference>>> Cache =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> CreationCounts =
        new(StringComparer.Ordinal);

    public static ImmutableArray<MetadataReference> GetReferences(
        IEnumerable<Assembly>? excludedAssemblies = null,
        IEnumerable<string>? excludedAssemblyNames = null,
        IEnumerable<string>? additionalLocations = null)
    {
        var profile = CreateProfile(excludedAssemblies, excludedAssemblyNames, additionalLocations);

        return Cache.GetOrAdd(
            profile.Identity,
            _ => new Lazy<ImmutableArray<MetadataReference>>(
                () => CreateReferences(profile.Identity, profile.Locations),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    internal static int GetCreationCount(
        IEnumerable<Assembly>? excludedAssemblies = null,
        IEnumerable<string>? excludedAssemblyNames = null,
        IEnumerable<string>? additionalLocations = null)
    {
        var identity = CreateProfile(excludedAssemblies, excludedAssemblyNames, additionalLocations).Identity;
        return CreationCounts.TryGetValue(identity, out var count) ? count : 0;
    }

    private static (string Identity, string[] Locations) CreateProfile(
        IEnumerable<Assembly>? excludedAssemblies,
        IEnumerable<string>? excludedAssemblyNames,
        IEnumerable<string>? additionalLocations)
    {
        var excludedLocations = (excludedAssemblies ?? Array.Empty<Assembly>())
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => Path.GetFullPath(assembly.Location))
            .ToHashSet(PathComparer);
        var excludedNames = (excludedAssemblyNames ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locations = BaseLocations.Value
            .Where(location => !excludedLocations.Contains(location))
            .Where(location => !excludedNames.Contains(Path.GetFileNameWithoutExtension(location)))
            .Concat(additionalLocations ?? Array.Empty<string>())
            .Append(typeof(object).Assembly.Location)
            .Append(typeof(Enumerable).Assembly.Location)
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(PathComparer)
            .OrderBy(static location => location, PathComparer)
            .ToArray();
        var identity = string.Join(
            "\n",
            new[] { AppContext.TargetFrameworkName ?? "unknown-tfm" }
                .Concat(locations));

        return (identity, locations);
    }

    private static ImmutableArray<string> CreateBaseLocations()
    {
        var trustedPlatformAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();
        var localAssemblies = Directory.EnumerateFiles(
            AppContext.BaseDirectory,
            "*.dll",
            SearchOption.TopDirectoryOnly);

        return trustedPlatformAssemblies
            .Concat(localAssemblies)
            .Append(typeof(object).Assembly.Location)
            .Append(typeof(Enumerable).Assembly.Location)
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(PathComparer)
            .OrderBy(static location => location, PathComparer)
            .ToImmutableArray();
    }

    private static ImmutableArray<MetadataReference> CreateReferences(
        string identity,
        IReadOnlyList<string> locations)
    {
        CreationCounts.AddOrUpdate(identity, 1, static (_, count) => count + 1);
        return locations
            .Select(static location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToImmutableArray();
    }
}
