using System;
using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Cache notification queue and sweep metrics.
/// </summary>
/// <param name="Subscriptions">Total number of notification subscriptions recorded.</param>
/// <param name="ApproximateCurrentQueueDepth">Approximate number of currently queued notification subscribers.</param>
/// <param name="LastNotifySnapshotEntries">Number of entries seen in the most recent notify sweep.</param>
/// <param name="LastNotifyLiveSubscribers">Number of live subscribers seen in the most recent notify sweep.</param>
/// <param name="NotifySweeps">Total number of notify sweeps recorded.</param>
/// <param name="NotifySnapshotEntries">Total number of notification entries observed by notify sweeps.</param>
/// <param name="NotifyLiveSubscribers">Total number of live subscribers observed by notify sweeps.</param>
/// <param name="LastCleanSnapshotEntries">Number of entries seen in the most recent clean sweep.</param>
/// <param name="LastCleanRequeuedSubscribers">Number of live subscribers requeued in the most recent clean sweep.</param>
/// <param name="LastCleanDroppedSubscribers">Number of dead subscribers dropped in the most recent clean sweep.</param>
/// <param name="CleanSweeps">Total number of clean sweeps recorded.</param>
/// <param name="CleanSnapshotEntries">Total number of notification entries inspected by clean sweeps.</param>
/// <param name="CleanRequeuedSubscribers">Total number of live subscribers requeued during clean sweeps.</param>
/// <param name="CleanDroppedSubscribers">Total number of dead subscribers dropped during clean sweeps.</param>
/// <param name="CleanBusySkips">Total number of clean sweeps skipped because notification maintenance was already busy.</param>
/// <param name="ApproximatePeakQueueDepth">Highest approximate queue depth observed.</param>
public readonly record struct CacheNotificationMetricsSnapshot(
    long Subscriptions,
    long ApproximateCurrentQueueDepth,
    long LastNotifySnapshotEntries,
    long LastNotifyLiveSubscribers,
    long NotifySweeps,
    long NotifySnapshotEntries,
    long NotifyLiveSubscribers,
    long LastCleanSnapshotEntries,
    long LastCleanRequeuedSubscribers,
    long LastCleanDroppedSubscribers,
    long CleanSweeps,
    long CleanSnapshotEntries,
    long CleanRequeuedSubscribers,
    long CleanDroppedSubscribers,
    long CleanBusySkips,
    long ApproximatePeakQueueDepth)
{
    internal static CacheNotificationMetricsSnapshot Sum(IEnumerable<CacheNotificationMetricsSnapshot> snapshots)
    {
        long subscriptions = 0;
        long approximateCurrentQueueDepth = 0;
        long lastNotifySnapshotEntries = 0;
        long lastNotifyLiveSubscribers = 0;
        long notifySweeps = 0;
        long notifySnapshotEntries = 0;
        long notifyLiveSubscribers = 0;
        long lastCleanSnapshotEntries = 0;
        long lastCleanRequeuedSubscribers = 0;
        long lastCleanDroppedSubscribers = 0;
        long cleanSweeps = 0;
        long cleanSnapshotEntries = 0;
        long cleanRequeuedSubscribers = 0;
        long cleanDroppedSubscribers = 0;
        long cleanBusySkips = 0;
        long approximatePeakQueueDepth = 0;

        foreach (var snapshot in snapshots)
        {
            subscriptions += snapshot.Subscriptions;
            approximateCurrentQueueDepth += snapshot.ApproximateCurrentQueueDepth;
            lastNotifySnapshotEntries += snapshot.LastNotifySnapshotEntries;
            lastNotifyLiveSubscribers += snapshot.LastNotifyLiveSubscribers;
            notifySweeps += snapshot.NotifySweeps;
            notifySnapshotEntries += snapshot.NotifySnapshotEntries;
            notifyLiveSubscribers += snapshot.NotifyLiveSubscribers;
            lastCleanSnapshotEntries += snapshot.LastCleanSnapshotEntries;
            lastCleanRequeuedSubscribers += snapshot.LastCleanRequeuedSubscribers;
            lastCleanDroppedSubscribers += snapshot.LastCleanDroppedSubscribers;
            cleanSweeps += snapshot.CleanSweeps;
            cleanSnapshotEntries += snapshot.CleanSnapshotEntries;
            cleanRequeuedSubscribers += snapshot.CleanRequeuedSubscribers;
            cleanDroppedSubscribers += snapshot.CleanDroppedSubscribers;
            cleanBusySkips += snapshot.CleanBusySkips;
            approximatePeakQueueDepth = Math.Max(approximatePeakQueueDepth, snapshot.ApproximatePeakQueueDepth);
        }

        return new CacheNotificationMetricsSnapshot(
            Subscriptions: subscriptions,
            ApproximateCurrentQueueDepth: approximateCurrentQueueDepth,
            LastNotifySnapshotEntries: lastNotifySnapshotEntries,
            LastNotifyLiveSubscribers: lastNotifyLiveSubscribers,
            NotifySweeps: notifySweeps,
            NotifySnapshotEntries: notifySnapshotEntries,
            NotifyLiveSubscribers: notifyLiveSubscribers,
            LastCleanSnapshotEntries: lastCleanSnapshotEntries,
            LastCleanRequeuedSubscribers: lastCleanRequeuedSubscribers,
            LastCleanDroppedSubscribers: lastCleanDroppedSubscribers,
            CleanSweeps: cleanSweeps,
            CleanSnapshotEntries: cleanSnapshotEntries,
            CleanRequeuedSubscribers: cleanRequeuedSubscribers,
            CleanDroppedSubscribers: cleanDroppedSubscribers,
            CleanBusySkips: cleanBusySkips,
            ApproximatePeakQueueDepth: approximatePeakQueueDepth);
    }
}
