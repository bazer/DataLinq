namespace DataLinq.Benchmark;

internal sealed record BenchmarkTelemetryDeltaArtifact(
    string Method,
    string ProviderName,
    int OperationsPerInvoke,
    double EntityQueriesPerOperation,
    double ScalarQueriesPerOperation,
    double RowCacheHitsPerOperation,
    double RowCacheMissesPerOperation,
    double RowCacheStoresPerOperation,
    double DatabaseRowsPerOperation,
    double MaterializationsPerOperation,
    double RelationHitsPerOperation,
    double RelationLoadsPerOperation);
