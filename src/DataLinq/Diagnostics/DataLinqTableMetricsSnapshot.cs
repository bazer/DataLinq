namespace DataLinq.Diagnostics;

/// <summary>
/// Metrics owned by a single table within a specific provider instance.
/// </summary>
/// <param name="TableName">Table name within the provider instance.</param>
/// <param name="Relations">Relation metrics owned by this table.</param>
/// <param name="RowCache">Row cache metrics owned by this table.</param>
/// <param name="CacheNotifications">Cache notification metrics owned by this table.</param>
public readonly record struct DataLinqTableMetricsSnapshot(
    string TableName,
    RelationMetricsSnapshot Relations,
    RowCacheMetricsSnapshot RowCache,
    CacheNotificationMetricsSnapshot CacheNotifications);
