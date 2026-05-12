using System;
using System.Collections.Concurrent;
using System.Threading;
using DataLinq.Diagnostics;
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
            Transaction? Transaction);

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
        {
            // This is a fully thread-safe, lock-free, O(1) operation.
            _subscribers.Enqueue(new CacheNotificationSubscription(new WeakReference<ICacheNotification>(subscriber), transaction));
            var approximateQueueDepth = Interlocked.Increment(ref _approximateSubscriberCount);
            metricsHandle.RecordCacheNotificationSubscribe(approximateQueueDepth);
        }

        internal void Notify() => Notify(null);

        internal void Notify(Transaction? transaction)
        {
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
                    if (transaction == null || ReferenceEquals(subscription.Transaction, transaction))
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

    protected virtual void OnRowChanged(Transaction? transaction = null)
    {
        notificationManager?.Notify(transaction);
    }

    public void CleanRelationNotifications()
    {
        notificationManager?.Clean();
    }
}
