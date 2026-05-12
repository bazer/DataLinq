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
    WarmGeneratedStaticGet,
    ColdRelationTraversal,
    WarmRelationTraversal,
    ScalarRowCacheAddGetRemove,
    RepeatedNonPrimaryKeyEqualityFetch,
    RepeatedInPredicateFetch,
    RepeatedScalarAny
}
