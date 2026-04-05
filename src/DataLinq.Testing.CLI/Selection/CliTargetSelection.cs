using System.Collections.Generic;
using System.Linq;
using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal sealed record CliTargetSelection(
    string? AliasName,
    IReadOnlyList<TestCliTarget> Targets)
{
    public IReadOnlyList<DatabaseServerTarget> ServerTargets =>
        Targets
            .Where(x => x.ServerTarget is not null)
            .Select(x => x.ServerTarget!)
            .ToArray();
}
