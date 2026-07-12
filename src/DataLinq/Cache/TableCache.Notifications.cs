using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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
            DataLinqKey[] LoadedPrimaryKeys,
            long NotificationBytes,
            long RelationObjectBytes)
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

        // Use ConcurrentQueue for O(1) subscriptions. Queue replacement and
        // subscription enqueue are serialized briefly so recovery can establish
        // a precise discard boundary without racing an enqueue into an old queue.
        // Notify invokes subscribers outside that gate, and Clean compacts dead
        // weak references for read-heavy workloads that don't notify often.
        private readonly DataLinqTableMetricsHandle metricsHandle;
        private ConcurrentQueue<CacheNotificationSubscription> _subscribers = new();
        private int _maintenanceState = 0;
        private int _approximateSubscriberCount = 0;
        private long _notificationBytes = 0;
        private long _relationObjectBytes = 0;
        private long _discardGeneration = 0;

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
            var loadedPrimaryKeyArray = loadedPrimaryKeys.Count == 0 ? [] : loadedPrimaryKeys.ToArray();
            var notificationBytes = CacheMemoryEstimator.SaturatingAdd(
                CacheMemoryEstimator.NotificationSubscriptionBytes,
                CacheMemoryEstimator.WeakReferenceBytes);
            var relationObjectBytes = EstimateRelationSubscriptionBytes(relationKey, loadedPrimaryKeyArray);

            EnterMaintenanceGate();
            try
            {
                _subscribers.Enqueue(new CacheNotificationSubscription(
                    new WeakReference<ICacheNotification>(subscriber),
                    transaction,
                    tableWide,
                    relationKey,
                    loadedPrimaryKeyArray,
                    notificationBytes,
                    relationObjectBytes));
                AddSubscriptionEstimate(notificationBytes, relationObjectBytes);
                var approximateQueueDepth = Interlocked.Increment(ref _approximateSubscriberCount);
                metricsHandle.RecordCacheNotificationSubscribe(approximateQueueDepth);
            }
            finally
            {
                ExitMaintenanceGate();
            }
        }

        internal CacheMemoryEstimate GetMemoryEstimate()
        {
            var approximateSubscriberCount = Volatile.Read(ref _approximateSubscriberCount);
            var notificationBytes = CacheMemoryEstimator.SaturatingAdd(
                Interlocked.Read(ref _notificationBytes),
                CacheMemoryEstimator.ConcurrentQueueOverheadBytes(approximateSubscriberCount));

            return new CacheMemoryEstimate(
                RelationObjectBytes: Interlocked.Read(ref _relationObjectBytes),
                NotificationBytes: notificationBytes);
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

            // 2. Serialize the queue swap with Subscribe(), Clean(), and Discard().
            // We only hold this gate long enough to take a private snapshot.
            EnterMaintenanceGate();

            ConcurrentQueue<CacheNotificationSubscription>? subscribersToNotify = null;
            long discardGeneration;
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
                discardGeneration = Volatile.Read(ref _discardGeneration);
            }
            finally
            {
                ExitMaintenanceGate();
            }

            // 4. Iterate over our private snapshot outside the maintenance gate.
            var snapshotEntries = 0;
            var liveSubscribers = 0;
            List<Exception>? notificationFailures = null;
            foreach (var subscription in subscribersToNotify)
            {
                snapshotEntries++;
                if (subscription.Subscriber.TryGetTarget(out var subscriber))
                {
                    if ((transaction == null || ReferenceEquals(subscription.Transaction, transaction)) &&
                        subscription.Matches(impact))
                    {
                        liveSubscribers++;
                        try
                        {
                            subscriber.Clear();
                        }
                        catch (Exception exception)
                        {
                            (notificationFailures ??= []).Add(exception);
                        }
                        finally
                        {
                            DropSubscriptionEstimate(subscription);
                        }
                    }
                    else
                    {
                        DropSubscriptionEstimate(subscription);
                        TryRequeueAfterNotify(subscription, discardGeneration);
                    }
                }
                else
                {
                    DropSubscriptionEstimate(subscription);
                }
            }

            var approximateQueueDepth = Volatile.Read(ref _approximateSubscriberCount);
            metricsHandle.RecordCacheNotificationNotifySweep(snapshotEntries, liveSubscribers, approximateQueueDepth);

            if (notificationFailures is { Count: 1 })
                ExceptionDispatchInfo.Capture(notificationFailures[0]).Throw();

            if (notificationFailures is { Count: > 1 })
                throw new AggregateException("Multiple cache notification subscribers failed to clear.", notificationFailures);
        }

        internal void Discard()
        {
            EnterMaintenanceGate();
            try
            {
                Interlocked.Increment(ref _discardGeneration);
                var subscribersToDiscard = Interlocked.Exchange(
                    ref _subscribers,
                    new ConcurrentQueue<CacheNotificationSubscription>());
                Interlocked.Exchange(ref _approximateSubscriberCount, 0);

                var discardedSubscribers = 0;
                foreach (var subscription in subscribersToDiscard)
                {
                    discardedSubscribers++;
                    DropSubscriptionEstimate(subscription);
                }

                if (discardedSubscribers > 0)
                {
                    metricsHandle.RecordCacheNotificationCleanSweep(
                        discardedSubscribers,
                        requeuedSubscribers: 0,
                        droppedSubscribers: discardedSubscribers,
                        currentQueueDepth: 0);
                }
            }
            finally
            {
                ExitMaintenanceGate();
            }
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
                        DropSubscriptionEstimate(subscription);
                        _subscribers.Enqueue(subscription);
                        AddSubscriptionEstimate(subscription);
                        requeuedSubscribers++;
                    }
                    else
                    {
                        DropSubscriptionEstimate(subscription);
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

        private static long EstimateRelationSubscriptionBytes(
            RelationCacheKey? relationKey,
            DataLinqKey[] loadedPrimaryKeys)
        {
            var bytes = 0L;

            if (relationKey is { } key)
            {
                bytes = CacheMemoryEstimator.SaturatingAdd(bytes, CacheMemoryEstimator.RelationSubscriptionKeyBytes);
                bytes = CacheMemoryEstimator.SaturatingAdd(bytes, CacheMemoryEstimator.EstimateDataLinqKeyPayloadBytes(key.ProviderKey));
            }

            if (loadedPrimaryKeys.Length > 0)
            {
                bytes = CacheMemoryEstimator.SaturatingAdd(bytes, CacheMemoryEstimator.DataLinqKeyArrayBytes(loadedPrimaryKeys.Length));
                for (var i = 0; i < loadedPrimaryKeys.Length; i++)
                    bytes = CacheMemoryEstimator.SaturatingAdd(bytes, CacheMemoryEstimator.EstimateDataLinqKeyPayloadBytes(loadedPrimaryKeys[i]));
            }

            return bytes;
        }

        private bool TryRequeueAfterNotify(
            CacheNotificationSubscription subscription,
            long discardGeneration)
        {
            EnterMaintenanceGate();
            try
            {
                if (Volatile.Read(ref _discardGeneration) != discardGeneration)
                    return false;

                _subscribers.Enqueue(subscription);
                AddSubscriptionEstimate(subscription);
                Interlocked.Increment(ref _approximateSubscriberCount);
                return true;
            }
            finally
            {
                ExitMaintenanceGate();
            }
        }

        private void EnterMaintenanceGate()
        {
            var spinWait = new SpinWait();
            while (Interlocked.CompareExchange(ref _maintenanceState, 1, 0) != 0)
                spinWait.SpinOnce();
        }

        private void ExitMaintenanceGate() =>
            Volatile.Write(ref _maintenanceState, 0);

        private void AddSubscriptionEstimate(CacheNotificationSubscription subscription) =>
            AddSubscriptionEstimate(subscription.NotificationBytes, subscription.RelationObjectBytes);

        private void AddSubscriptionEstimate(long notificationBytes, long relationObjectBytes)
        {
            Interlocked.Add(ref _notificationBytes, notificationBytes);
            Interlocked.Add(ref _relationObjectBytes, relationObjectBytes);
        }

        private void DropSubscriptionEstimate(CacheNotificationSubscription subscription)
        {
            Interlocked.Add(ref _notificationBytes, -subscription.NotificationBytes);
            Interlocked.Add(ref _relationObjectBytes, -subscription.RelationObjectBytes);
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

    internal CacheMemoryEstimate GetNotificationMemoryEstimate() =>
        notificationManager?.GetMemoryEstimate() ?? CacheMemoryEstimate.Empty;

    internal void DiscardRecoveryNotifications() =>
        notificationManager?.Discard();
}
