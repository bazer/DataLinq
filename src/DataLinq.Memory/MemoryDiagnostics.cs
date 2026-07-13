namespace DataLinq.Memory;

internal readonly record struct MemoryDiagnostics(
    long PrimaryKeyRequests,
    long PrimaryKeyProbes,
    long ScanRowsVisited,
    long CacheLookups,
    long CacheHits,
    long CacheMisses,
    long Materializations,
    long CacheInsertions);
