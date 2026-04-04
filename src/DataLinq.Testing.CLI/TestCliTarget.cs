using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal sealed record TestCliTarget(
    string Id,
    string DisplayName,
    bool UsesPodman,
    string Category,
    DatabaseServerTarget? ServerTarget);
