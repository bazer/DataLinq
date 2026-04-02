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
}
