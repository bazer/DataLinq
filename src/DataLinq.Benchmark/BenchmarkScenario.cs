namespace DataLinq.Benchmark;

internal enum BenchmarkScenario
{
    StartupPrimaryKeyFetch,
    InsertEmployeesBatch,
    UpdateEmployeesBatch,
    ColdPrimaryKeyFetch,
    WarmPrimaryKeyFetch,
    ColdRelationTraversal,
    WarmRelationTraversal
}
