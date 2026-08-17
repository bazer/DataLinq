using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Testing.CLI;

internal sealed record TestCliRunPlanSuite(
    string Suite,
    string? Filter,
    int ExpectedCaseCount,
    double EstimatedDurationSeconds,
    string Purpose,
    string Resource);

internal sealed record TestCliRunPlan(
    string Name,
    string Description,
    string Command,
    string Prerequisites,
    int WarmBudgetSeconds,
    string? DefaultTargetAlias,
    IReadOnlyList<string> DefaultTargetIds,
    IReadOnlyList<TestCliRunPlanSuite> Suites,
    bool RequiresExplicitSelection = false)
{
    public int ExpectedCaseCount => Suites.Sum(static suite => suite.ExpectedCaseCount);

    public double EstimatedDurationSeconds => Suites.Sum(static suite => suite.EstimatedDurationSeconds);
}
