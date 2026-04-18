namespace DataLinq.Benchmark;

internal enum BenchmarkScenario
{
    StartupPrimaryKeyFetch,
    ColdPrimaryKeyFetch,
    WarmPrimaryKeyFetch,
    ColdRelationTraversal,
    WarmRelationTraversal
}
