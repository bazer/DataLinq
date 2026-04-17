using DataLinq.Metadata;

namespace DataLinq.Diagnostics;

/// <summary>
/// Metrics owned by a single database provider instance.
/// </summary>
/// <param name="ProviderInstanceId">Stable identifier for the provider instance within the current process lifetime.</param>
/// <param name="ProviderTypeName">Runtime type name of the provider instance.</param>
/// <param name="DatabaseName">Logical database name reported by the provider instance.</param>
/// <param name="DatabaseType">Database type reported by the provider instance.</param>
/// <param name="Queries">Query metrics owned by this provider instance.</param>
/// <param name="Relations">Relation metrics summed from this provider instance's tables.</param>
/// <param name="RowCache">Row cache metrics summed from this provider instance's tables.</param>
/// <param name="CacheNotifications">Cache notification metrics summed from this provider instance's tables.</param>
/// <param name="Tables">Per-table metrics for this provider instance.</param>
public readonly record struct DataLinqProviderMetricsSnapshot(
    string ProviderInstanceId,
    string ProviderTypeName,
    string DatabaseName,
    DatabaseType DatabaseType,
    QueryMetricsSnapshot Queries,
    RelationMetricsSnapshot Relations,
    RowCacheMetricsSnapshot RowCache,
    CacheNotificationMetricsSnapshot CacheNotifications,
    DataLinqTableMetricsSnapshot[] Tables);
