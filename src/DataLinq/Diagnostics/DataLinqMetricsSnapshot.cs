namespace DataLinq.Diagnostics;

/// <summary>
/// Process-wide snapshot of DataLinq metrics for the current runtime instance.
/// </summary>
/// <param name="Queries">Query metrics summed across all provider instances.</param>
/// <param name="Commands">Command metrics summed across all provider instances.</param>
/// <param name="Transactions">Transaction metrics summed across all provider instances.</param>
/// <param name="Mutations">Mutation metrics summed across all provider instances.</param>
/// <param name="Occupancy">Current cache occupancy summed across all provider instances.</param>
/// <param name="Cleanup">Cache maintenance metrics summed across all provider instances.</param>
/// <param name="Relations">Relation metrics summed across all provider instances.</param>
/// <param name="RowCache">Row cache metrics summed across all provider instances.</param>
/// <param name="CacheNotifications">Cache notification metrics summed across all provider instances.</param>
/// <param name="Providers">Per-provider metrics for the current DataLinq runtime instance.</param>
public readonly record struct DataLinqMetricsSnapshot(
    QueryMetricsSnapshot Queries,
    CommandMetricsSnapshot Commands,
    TransactionMetricsSnapshot Transactions,
    MutationMetricsSnapshot Mutations,
    CacheOccupancyMetricsSnapshot Occupancy,
    CacheCleanupMetricsSnapshot Cleanup,
    RelationMetricsSnapshot Relations,
    RowCacheMetricsSnapshot RowCache,
    CacheNotificationMetricsSnapshot CacheNotifications,
    DataLinqProviderMetricsSnapshot[] Providers)
{
    public override string ToString()
        => $"entity-queries={Queries.EntityExecutions}, scalar-queries={Queries.ScalarExecutions}, " +
           $"db-command-total={Commands.TotalExecutions}, db-command-reader={Commands.ReaderExecutions}, " +
           $"db-command-scalar={Commands.ScalarExecutions}, db-command-non-query={Commands.NonQueryExecutions}, " +
           $"db-command-failures={Commands.Failures}, db-command-duration-ms={Commands.TotalDurationMilliseconds:0.###}, " +
           $"tx-starts={Transactions.Starts}, tx-commits={Transactions.Commits}, tx-rollbacks={Transactions.Rollbacks}, " +
           $"tx-failures={Transactions.Failures}, tx-duration-ms={Transactions.TotalDurationMilliseconds:0.###}, " +
           $"mutation-total={Mutations.TotalExecutions}, mutation-inserts={Mutations.Inserts}, mutation-updates={Mutations.Updates}, " +
           $"mutation-deletes={Mutations.Deletes}, mutation-failures={Mutations.Failures}, mutation-affected-rows={Mutations.AffectedRows}, " +
           $"mutation-duration-ms={Mutations.TotalDurationMilliseconds:0.###}, " +
           $"cache-rows-current={Occupancy.Rows}, cache-transaction-rows-current={Occupancy.TransactionRows}, " +
           $"cache-bytes-current={Occupancy.Bytes}, cache-index-entries-current={Occupancy.IndexEntries}, " +
           $"cache-cleanup-ops={Cleanup.Operations}, cache-cleanup-rows-removed={Cleanup.RowsRemoved}, " +
           $"cache-cleanup-duration-ms={Cleanup.TotalDurationMilliseconds:0.###}, " +
           $"row-cache-hits={RowCache.Hits}, row-cache-misses={RowCache.Misses}, database-rows={RowCache.DatabaseRowsLoaded}, " +
           $"materializations={RowCache.Materializations}, row-cache-stores={RowCache.Stores}, " +
           $"relation-ref-hits={Relations.ReferenceCacheHits}, relation-ref-loads={Relations.ReferenceLoads}, " +
           $"relation-collection-hits={Relations.CollectionCacheHits}, relation-collection-loads={Relations.CollectionLoads}, " +
           $"cache-notification-subscriptions={CacheNotifications.Subscriptions}, " +
           $"cache-notification-approx-current-depth={CacheNotifications.ApproximateCurrentQueueDepth}, " +
           $"cache-notification-last-notify-snapshot-entries={CacheNotifications.LastNotifySnapshotEntries}, " +
           $"cache-notification-last-notify-live={CacheNotifications.LastNotifyLiveSubscribers}, " +
           $"cache-notification-notify-sweeps={CacheNotifications.NotifySweeps}, " +
           $"cache-notification-notify-snapshot-entries={CacheNotifications.NotifySnapshotEntries}, " +
           $"cache-notification-notify-live={CacheNotifications.NotifyLiveSubscribers}, " +
           $"cache-notification-last-clean-snapshot-entries={CacheNotifications.LastCleanSnapshotEntries}, " +
           $"cache-notification-last-clean-requeued={CacheNotifications.LastCleanRequeuedSubscribers}, " +
           $"cache-notification-last-clean-dropped={CacheNotifications.LastCleanDroppedSubscribers}, " +
           $"cache-notification-clean-sweeps={CacheNotifications.CleanSweeps}, " +
           $"cache-notification-clean-snapshot-entries={CacheNotifications.CleanSnapshotEntries}, " +
           $"cache-notification-clean-requeued={CacheNotifications.CleanRequeuedSubscribers}, " +
           $"cache-notification-clean-dropped={CacheNotifications.CleanDroppedSubscribers}, " +
           $"cache-notification-clean-busy-skips={CacheNotifications.CleanBusySkips}, " +
           $"cache-notification-approx-peak-depth={CacheNotifications.ApproximatePeakQueueDepth}, " +
           $"providers={Providers.Length}";
}
