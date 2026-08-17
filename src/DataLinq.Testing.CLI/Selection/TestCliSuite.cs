namespace DataLinq.Testing.CLI;

internal sealed record TestCliSuite(
    string Name,
    string Description,
    string ProjectPath,
    bool UsesTargetBatches,
    bool IncludeSqliteTargets,
    string? Filter = null,
    int? ExpectedCaseCount = null,
    double? EstimatedDurationSeconds = null,
    string? Purpose = null,
    string? Resource = null,
    int? MaximumParallelTests = null);
