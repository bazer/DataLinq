using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Testing;

public static class TestTargetCatalog
{
    public const string SQLiteFileTargetId = "sqlite-file";
    public const string SQLiteMemoryTargetId = "sqlite-memory";
    public const string QuickAlias = "quick";
    public const string LatestAlias = "latest";
    public const string AllAlias = "all";

    private static readonly Lazy<IReadOnlyList<string>> AllTargetIdsLazy = new(() =>
        new ReadOnlyCollection<string>(
            [SQLiteFileTargetId, SQLiteMemoryTargetId, .. DatabaseServerMatrix.Targets.Select(x => x.Id)]));

    public static IReadOnlyList<string> AllTargetIds => AllTargetIdsLazy.Value;

    public static IReadOnlyList<string> ResolveAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("A target alias must be provided.", nameof(alias));

        return alias.ToLowerInvariant() switch
        {
            QuickAlias => [SQLiteFileTargetId, SQLiteMemoryTargetId],
            LatestAlias => [SQLiteFileTargetId, SQLiteMemoryTargetId, GetLatestMySqlTarget().Id, GetLatestMariaDbTarget().Id],
            AllAlias => AllTargetIds,
            _ => throw new InvalidOperationException($"Unknown test target alias '{alias}'.")
        };
    }

    public static bool IsSQLiteTarget(string targetId) =>
        string.Equals(targetId, SQLiteFileTargetId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(targetId, SQLiteMemoryTargetId, StringComparison.OrdinalIgnoreCase);

    public static DatabaseServerTarget? TryGetServerTarget(string targetId)
    {
        if (IsSQLiteTarget(targetId))
            return null;

        return DatabaseServerMatrix.Targets.SingleOrDefault(x => string.Equals(x.Id, targetId, StringComparison.OrdinalIgnoreCase));
    }

    private static DatabaseServerTarget GetLatestMySqlTarget() =>
        DatabaseServerMatrix.Targets
            .Where(x => x.Family == DatabaseServerFamily.MySql && x.IsLts)
            .OrderByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .First();

    private static DatabaseServerTarget GetLatestMariaDbTarget() =>
        DatabaseServerMatrix.Targets
            .Where(x => x.Family == DatabaseServerFamily.MariaDb && x.IsLts)
            .OrderByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .First();
}
