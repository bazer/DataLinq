namespace DataLinq.Testing.CLI;

internal sealed record TestCliSuite(
    string Name,
    string Description,
    string ProjectPath,
    bool UsesTargetBatches,
    bool IncludeSqliteTargets = true);
