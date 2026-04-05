using System.Collections.Generic;

namespace DataLinq.Testing.CLI;

internal sealed record TestCliAlias(
    string Name,
    string Description,
    IReadOnlyList<string> TargetIds);
