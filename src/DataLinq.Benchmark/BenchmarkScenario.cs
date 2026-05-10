namespace DataLinq.Benchmark;

internal enum BenchmarkScenario
{
    ProviderInitialization,
    StartupPrimaryKeyFetch,
    CrudWorkflowSmall,
    CrudWorkflowBatch,
    InsertEmployeesBatch,
    UpdateEmployeesBatch,
    ColdPrimaryKeyFetch,
    WarmPrimaryKeyFetch,
    ColdRelationTraversal,
    WarmRelationTraversal,
    RepeatedNonPrimaryKeyEqualityFetch,
    RepeatedInPredicateFetch,
    RepeatedScalarAny
}
