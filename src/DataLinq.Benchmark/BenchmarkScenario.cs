namespace DataLinq.Benchmark;

internal enum BenchmarkScenario
{
    ProviderInitialization,
    StartupPrimaryKeyFetch,
    InsertEmployeesBatch,
    UpdateEmployeesBatch,
    ColdPrimaryKeyFetch,
    WarmPrimaryKeyFetch,
    ColdRelationTraversal,
    WarmRelationTraversal
}
