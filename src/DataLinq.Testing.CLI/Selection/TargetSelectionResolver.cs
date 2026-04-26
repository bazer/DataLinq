using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Testing.CLI;

internal static class TargetSelectionResolver
{
    public static CliTargetSelection Resolve(string? aliasName, string? targetList, string? defaultAlias = null)
    {
        if (!string.IsNullOrWhiteSpace(aliasName) && !string.IsNullOrWhiteSpace(targetList))
            throw new InvalidOperationException("Use either '--alias' or '--targets', not both.");

        if (!string.IsNullOrWhiteSpace(aliasName))
            return ResolveAlias(aliasName);

        if (!string.IsNullOrWhiteSpace(targetList))
            return ResolveTargets(targetList);

        return defaultAlias is null
            ? ResolveTargets(TestCliCatalog.Targets.Select(x => x.Id))
            : ResolveAlias(defaultAlias);
    }

    public static CliTargetSelection ResolveAlias(string aliasName)
    {
        var alias = TestCliCatalog.GetAlias(aliasName);
        return new CliTargetSelection(alias.Name, ResolveTargets(alias.TargetIds).Targets);
    }

    public static CliTargetSelection ResolveTargets(string targetList)
    {
        var targetIds = targetList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return ResolveTargets(targetIds);
    }

    public static CliTargetSelection ResolveTargets(IEnumerable<string> targetIds)
    {
        var targets = targetIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(TestCliCatalog.GetTarget)
            .ToArray();

        return new CliTargetSelection(AliasName: null, Targets: targets);
    }
}
