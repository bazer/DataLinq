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
    InvalidateOneEmployeeRow,
    InvalidateManyEmployeeRows,
    InvalidateEmployeeTable,
    InvalidateDatabase,
    RepeatedNonPrimaryKeyEqualityFetch,
    RepeatedInPredicateFetch,
    RepeatedScalarAny
}
