using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Cache;

public interface ICacheNotification
{
    void Clear();
}

public partial class TableCache
{
    internal sealed class CacheNotificationManager
    {
        private sealed record CacheNotificationSubscription(
            WeakReference<ICacheNotification> Subscriber,
            Transaction? Transaction,
            bool TableWide,
            RelationCacheKey? RelationKey,
            DataLinqKey[] LoadedPrimaryKeys)
        {
            internal bool Matches(CacheInvalidationImpact impact)
            {
                if (TableWide || impact.ClearTable)
                    return true;

                if (RelationKey is { } relationKey)
                {
                    if (impact.ChangedRelationKeys.Contains(relationKey))
                        return true;

                    if (IsPrimaryKeyIndex(relationKey.Index) &&
                        impact.ChangedPrimaryKeys.Contains(relationKey.ProviderKey))
                    {
                        return true;
                    }
                }

                for (var i = 0; i < LoadedPrimaryKeys.Length; i++)
                {
                    if (impact.ChangedPrimaryKeys.Contains(LoadedPrimaryKeys[i]))
                        return true;
                }

                return false;
            }

            private static bool IsPrimaryKeyIndex(ColumnIndex index) =>
                index.Columns.Count == index.Table.PrimaryKeyColumns.Count &&
                index.Columns.SequenceEqual(index.Table.PrimaryKeyColumns);
        }

        // Use ConcurrentQueue. Subscribe stays lock-free and O(1).
        // Notify self-clears by swapping the queue, and Clean compacts dead
        // weak references for read-heavy workloads that don't notify often.
        private readonly DataLinqTableMetricsHandle metricsHandle;
        private ConcurrentQueue<CacheNotificationSubscription> _subscribers = new();
        private int _maintenanceState = 0;
        private int _approximateSubscriberCount = 0;

        internal CacheNotificationManager(DataLinqTableMetricsHandle metricsHandle)
        {
            this.metricsHandle = metricsHandle;
        }

        internal void Subscribe(ICacheNotification subscriber) => Subscribe(subscriber, null);

        internal void Subscribe(ICacheNotification subscriber, Transaction? transaction)
            => Subscribe(subscriber, transaction, tableWide: true, relationKey: null, loadedPrimaryKeys: []);

        internal void Subscribe(
            ICacheNotification subscriber,
            Transaction? transaction,
            RelationCacheKey? relationKey,
            IReadOnlyCollection<DataLinqKey> loadedPrimaryKeys)
            => Subscribe(subscriber, transaction, tableWide: false, relationKey, loadedPrimaryKeys);

        private void Subscribe(
            ICacheNotification subscriber,
            Transaction? transaction,
            bool tableWide,
            RelationCacheKey? relationKey,
            IReadOnlyCollection<DataLinqKey> loadedPrimaryKeys)
        {
            // This is a fully thread-safe, lock-free, O(1) operation.
            _subscribers.Enqueue(new CacheNotificationSubscription(
                new WeakReference<ICacheNotification>(subscriber),
                transaction,
                tableWide,
                relationKey,
                loadedPrimaryKeys.Count == 0 ? [] : loadedPrimaryKeys.ToArray()));
            var approximateQueueDepth = Interlocked.Increment(ref _approximateSubscriberCount);
            metricsHandle.RecordCacheNotificationSubscribe(approximateQueueDepth);
        }

        internal void Notify() => Notify(null);

        internal void Notify(Transaction? transaction) => Notify(CacheInvalidationImpact.TableWide, transaction);

        internal void Notify(CacheInvalidationImpact impact, Transaction? transaction = null)
        {
            if (impact.IsEmpty)
                return;

            // 1. Check if there's anything to do. This is a quick, lock-free check.
            if (_subscribers.IsEmpty)
            {
                return;
            }

            // 2. Serialize the queue swap with Clean() without blocking Subscribe().
            // We only hold this gate long enough to take a private snapshot.
            var spinWait = new SpinWait();
            while (Interlocked.CompareExchange(ref _maintenanceState, 1, 0) != 0)
                spinWait.SpinOnce();

            ConcurrentQueue<CacheNotificationSubscription>? subscribersToNotify = null;
            try
            {
                // Another maintenance operation may already have swapped the queue
                // while we were waiting, so re-check after acquiring the gate.
                if (_subscribers.IsEmpty)
                {
                    return;
                }

                // 3. Atomically swap the current queue with a new, empty one.
                // Any new calls to Subscribe() from other threads will now add
                // to the new queue without interfering with this notification pass.
                subscribersToNotify = Interlocked.Exchange(ref _subscribers, new ConcurrentQueue<CacheNotificationSubscription>());
                Interlocked.Exchange(ref _approximateSubscriberCount, 0);
            }
            finally
            {
                Volatile.Write(ref _maintenanceState, 0);
            }

            // 4. Iterate over our private snapshot outside the maintenance gate.
            var snapshotEntries = 0;
            var liveSubscribers = 0;
            var requeuedSubscribers = 0;
            foreach (var subscription in subscribersToNotify)
            {
                snapshotEntries++;
                if (subscription.Subscriber.TryGetTarget(out var subscriber))
                {
                    if ((transaction == null || ReferenceEquals(subscription.Transaction, transaction)) &&
                        subscription.Matches(impact))
                    {
                        liveSubscribers++;
                        subscriber.Clear();
                    }
                    else
                    {
                        _subscribers.Enqueue(subscription);
                        requeuedSubscribers++;
                    }
                }
            }

            var approximateQueueDepth = Interlocked.Add(ref _approximateSubscriberCount, requeuedSubscribers);
            metricsHandle.RecordCacheNotificationNotifySweep(snapshotEntries, liveSubscribers, approximateQueueDepth);
        }

        internal void Clean()
        {
            // Best-effort compaction. If Notify() is already in progress, skip this
            // cycle and let the next background sweep retry.
            if (_subscribers.IsEmpty || Interlocked.CompareExchange(ref _maintenanceState, 1, 0) != 0)
            {
                if (!_subscribers.IsEmpty)
                    metricsHandle.RecordCacheNotificationCleanBusySkip();
                return;
            }

            try
            {
                // Clean keeps the maintenance gate for the full compaction pass.
                // If Notify() were allowed to swap the queue between our Exchange()
                // and re-enqueue of live subscribers, it could miss an invalidation.
                if (_subscribers.IsEmpty)
                {
                    return;
                }

                var subscribersToKeep = Interlocked.Exchange(ref _subscribers, new ConcurrentQueue<CacheNotificationSubscription>());
                Interlocked.Exchange(ref _approximateSubscriberCount, 0);
                var snapshotEntries = 0;
                var requeuedSubscribers = 0;
                foreach (var subscription in subscribersToKeep)
                {
                    snapshotEntries++;
                    if (subscription.Subscriber.TryGetTarget(out _))
                    {
                        _subscribers.Enqueue(subscription);
                        requeuedSubscribers++;
                    }
                }

                var approximateQueueDepth = Interlocked.Add(ref _approximateSubscriberCount, requeuedSubscribers);
                metricsHandle.RecordCacheNotificationCleanSweep(
                    snapshotEntries,
                    requeuedSubscribers,
                    snapshotEntries - requeuedSubscribers,
                    approximateQueueDepth);
            }
            finally
            {
                Volatile.Write(ref _maintenanceState, 0);
            }
        }
    }

    public void SubscribeToChanges(ICacheNotification subscriber, Transaction? transaction = null)
    {
        (notificationManager ??= new CacheNotificationManager(MetricsHandle)).Subscribe(subscriber, transaction);
    }

    internal void SubscribeToChanges(
        ICacheNotification subscriber,
        Transaction? transaction,
        RelationCacheKey? relationKey,
        IReadOnlyCollection<DataLinqKey> loadedPrimaryKeys)
    {
        (notificationManager ??= new CacheNotificationManager(MetricsHandle)).Subscribe(subscriber, transaction, relationKey, loadedPrimaryKeys);
    }

    protected virtual void OnRowChanged(Transaction? transaction = null)
    {
        notificationManager?.Notify(transaction);
    }

    private void OnRowChanged(CacheInvalidationImpact impact, Transaction? transaction = null)
    {
        notificationManager?.Notify(impact, transaction);
    }

    public void CleanRelationNotifications()
    {
        notificationManager?.Clean();
    }
}
