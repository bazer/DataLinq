using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DataLinq;

namespace DataLinq.Testing;

public static class TestProviderMatrix
{
    public static TestProviderDescriptor SQLiteFile { get; } = new(
        Name: "sqlite-file",
        Kind: TestProviderKind.SQLiteFile,
        DatabaseType: DatabaseType.SQLite,
        RequiresExternalServer: false,
        UsesPodman: false,
        ServerTarget: null);

    public static TestProviderDescriptor SQLiteInMemory { get; } = new(
        Name: "sqlite-memory",
        Kind: TestProviderKind.SQLiteInMemory,
        DatabaseType: DatabaseType.SQLite,
        RequiresExternalServer: false,
        UsesPodman: false,
        ServerTarget: null);

    public static IReadOnlyList<TestProviderDescriptor> SQLiteOnly { get; } = new ReadOnlyCollection<TestProviderDescriptor>(
    [
        SQLiteFile,
        SQLiteInMemory
    ]);

    public static IReadOnlyList<TestProviderDescriptor> AllLtsServerProviders =>
        new ReadOnlyCollection<TestProviderDescriptor>(
            DatabaseServerMatrix.Targets
                .Where(x => x.IsLts)
                .Select(CreateServerProvider)
                .ToArray());

    public static IReadOnlyList<TestProviderDescriptor> All =>
        new ReadOnlyCollection<TestProviderDescriptor>(SQLiteOnly.Concat(AllLtsServerProviders).ToArray());

    public static IReadOnlyList<TestProviderDescriptor> ForProfile(DatabaseServerProfile profile)
    {
        return new ReadOnlyCollection<TestProviderDescriptor>(
            SQLiteOnly.Concat(profile.Targets.Select(CreateServerProvider)).ToArray());
    }

    public static IReadOnlyList<TestProviderDescriptor> ForActiveProfile(PodmanTestEnvironmentSettings settings) =>
        ForProfile(settings.ActiveProfile);

    public static IReadOnlyList<TestProviderDescriptor> ForCurrentRun(PodmanTestEnvironmentSettings settings)
    {
        return settings.ProviderSet.ToLowerInvariant() switch
        {
            "quick" => ProvidersForTargetIds(TestTargetCatalog.ResolveAlias(TestTargetCatalog.QuickAlias)),
            "latest" => ProvidersForTargetIds(TestTargetCatalog.ResolveAlias(TestTargetCatalog.LatestAlias)),
            "alias" => ProvidersForTargetIds(settings.SelectedTargetIds),
            "fast" => FastForProfile(settings.ActiveProfile),
            "profile" => ForProfile(settings.ActiveProfile),
            "targets" or "batch" => TargetsForCurrentRun(settings),
            "all" or "all-lts" or "full" => AllLtsForCurrentRun(settings),
            _ => FastForProfile(settings.ActiveProfile)
        };
    }

    public static IReadOnlyList<TestProviderDescriptor> FastForProfile(DatabaseServerProfile profile)
    {
        var primaryServerTarget = profile.MariaDbTarget
            ?? profile.Targets.FirstOrDefault(x => x.Family == DatabaseServerFamily.MariaDb && x.IsDefault)
            ?? profile.MySqlTarget
            ?? profile.Targets.FirstOrDefault(x => x.IsDefault)
            ?? profile.Targets.FirstOrDefault();

        var providers = primaryServerTarget is null
            ? SQLiteOnly
            : SQLiteOnly.Concat([CreateServerProvider(primaryServerTarget)]);

        return new ReadOnlyCollection<TestProviderDescriptor>(providers.ToArray());
    }

    private static IReadOnlyList<TestProviderDescriptor> TargetsForCurrentRun(PodmanTestEnvironmentSettings settings)
    {
        return ProvidersForTargetIds(settings.SelectedTargetIds);
    }

    private static IReadOnlyList<TestProviderDescriptor> AllLtsForCurrentRun(PodmanTestEnvironmentSettings settings)
    {
        var requiredTargets = DatabaseServerMatrix.Targets
            .Where(x => x.IsLts)
            .Select(x => x.Id)
            .OrderBy(x => x, System.StringComparer.Ordinal)
            .ToArray();

        var availableTargets = settings.GetAvailableServerTargets()
            .Select(x => x.Id)
            .OrderBy(x => x, System.StringComparer.Ordinal)
            .ToArray();

        var missingTargets = requiredTargets
            .Except(availableTargets, System.StringComparer.Ordinal)
            .ToArray();

        if (missingTargets.Length > 0)
        {
            throw new InvalidOperationException(
                $"The requested provider set '{settings.ProviderSet}' requires all LTS server targets, " +
                $"but the currently provisioned Podman targets only expose [{string.Join(", ", availableTargets)}]. " +
                $"Missing targets: [{string.Join(", ", missingTargets)}]. " +
                $"Run 'dotnet run --project src\\DataLinq.Testing.CLI -- up --alias all' to provision every LTS target locally, " +
                $"or use 'dotnet run --project src\\DataLinq.Testing.CLI -- run --alias all --batch-size <n>' to fan out across the matrix in batches.");
        }

        return All;
    }

    private static TestProviderDescriptor CreateServerProvider(DatabaseServerTarget target)
    {
        return new TestProviderDescriptor(
            Name: target.Id,
            Kind: TestProviderKind.Server,
            DatabaseType: target.DatabaseType,
            RequiresExternalServer: true,
            UsesPodman: true,
            ServerTarget: target);
    }

    private static IReadOnlyList<TestProviderDescriptor> ProvidersForTargetIds(IEnumerable<string> targetIds)
    {
        var providers = targetIds
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Select(static targetId => targetId.ToLowerInvariant() switch
            {
                TestTargetCatalog.SQLiteFileTargetId => SQLiteFile,
                TestTargetCatalog.SQLiteMemoryTargetId => SQLiteInMemory,
                _ => CreateServerProvider(DatabaseServerMatrix.GetTarget(targetId))
            })
            .ToArray();

        return new ReadOnlyCollection<TestProviderDescriptor>(providers);
    }
}
