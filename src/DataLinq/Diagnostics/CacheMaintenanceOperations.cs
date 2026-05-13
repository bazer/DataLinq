namespace DataLinq.Diagnostics;

internal static class CacheMaintenanceOperations
{
    public const string StateChangePrecise = "state_change_precise";
    public const string StateChangeTable = "state_change_table";
    public const string TransactionStateChange = "transaction_state_change";
    public const string TransactionStateChangeTable = "transaction_state_change_table";
    public const string TransactionRemove = "transaction_remove";
    public const string ManualInvalidate = "manual_invalidate";
    public const string Clear = "clear";
    public const string RowLimit = "row_limit";
    public const string SizeLimit = "size_limit";
    public const string AgeLimit = "age_limit";
    public const string MemoryPressure = "memory_pressure";
    public const string Limit = "limit";
}

internal static class CacheMaintenanceTriggers
{
    public const string Unknown = "unknown";
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
    public const string Mutation = "mutation";
    public const string Transaction = "transaction";
    public const string MemoryPressure = "memory_pressure";
}

internal static class CacheMaintenanceReasons
{
    public const string Unknown = "unknown";
    public const string Manual = "manual";
    public const string Clear = "clear";
    public const string RowLimit = "row_limit";
    public const string SizeLimit = "size_limit";
    public const string AgeLimit = "age_limit";
    public const string Limit = "limit";
    public const string StateChange = "state_change";
    public const string Transaction = "transaction";
    public const string MemoryPressure = "memory_pressure";
}

internal static class CacheMaintenanceBases
{
    public const string Unknown = "unknown";
    public const string Manual = "manual";
    public const string RowCount = "row_count";
    public const string EstimatedCacheBytes = "estimated_cache_bytes";
    public const string CacheAge = "cache_age";
    public const string StateChange = "state_change";
    public const string Transaction = "transaction";
    public const string None = "none";
}
