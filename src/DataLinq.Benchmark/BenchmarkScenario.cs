namespace DataLinq.Benchmark;

internal enum BenchmarkScenario
{
    ColdPrimaryKeyFetch,
    WarmPrimaryKeyFetch,
    ColdRelationTraversal,
    WarmRelationTraversal
}
