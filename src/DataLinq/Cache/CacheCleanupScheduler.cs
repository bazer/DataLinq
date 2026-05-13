using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;

namespace DataLinq.Cache;

public sealed class CacheCleanupScheduler : IDisposable
{
    private readonly object lifecycleGate = new();
    private readonly DatabaseCache cache;
    private readonly TimeProvider timeProvider;
    private readonly List<ScheduledCleanupInterval> schedules;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? workerTask;

    internal CacheCleanupScheduler(
        DatabaseCache cache,
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)> cleanupIntervals,
        TimeProvider timeProvider)
    {
        this.cache = cache;
        this.timeProvider = timeProvider;
        schedules = cleanupIntervals
            .Select(x => new ScheduledCleanupInterval(ConvertCleanupIntervalToTimeSpan(x.cleanupType, x.amount)))
            .Where(x => x.Interval > TimeSpan.Zero)
            .OrderBy(x => x.Interval)
            .ToList();
    }

    public int ActiveScheduleCount => schedules.Count;
    public bool IsRunning => Volatile.Read(ref workerTask) is { IsCompleted: false };

    internal int BackgroundWorkerCount => IsRunning ? 1 : 0;

    public void Start()
    {
        if (schedules.Count == 0)
            return;

        lock (lifecycleGate)
        {
            if (workerTask is { IsCompleted: false })
                return;

            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;
            workerTask = Task.Factory.StartNew(
                () => RunLoopAsync(token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? source;
        Task? task;

        lock (lifecycleGate)
        {
            source = cancellationTokenSource;
            task = workerTask;
            cancellationTokenSource = null;
            workerTask = null;
        }

        source?.Cancel();

        if (task is not null &&
            task.Id != Task.CurrentId &&
            !task.IsCompleted)
        {
            try
            {
                task.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException exception) when (exception.InnerExceptions.All(x => x is OperationCanceledException))
            {
            }
        }

        source?.Dispose();
    }

    internal CacheCleanupPassResult RunScheduledCleanup()
    {
        var beforeEstimate = cache.GetMemoryEstimate().EstimatedCacheBytes;
        cache.CleanRelationNotifications();
        var removedRows = cache.RemoveRowsBySettings(CacheMaintenanceTriggers.Scheduled).Sum(x => x.numRows);
        var afterEstimate = cache.GetMemoryEstimate().EstimatedCacheBytes;

        return new CacheCleanupPassResult(
            CacheMaintenanceReasons.AgeLimit,
            CacheMaintenanceTriggers.Scheduled,
            CacheMaintenanceBases.CacheAge,
            removedRows,
            beforeEstimate,
            afterEstimate,
            TargetEstimatedCacheBytes: null);
    }

    internal void RunDueScheduledCleanup(DateTimeOffset now)
    {
        if (schedules.Count == 0)
            return;

        var hasDueSchedule = false;
        for (var i = 0; i < schedules.Count; i++)
        {
            var schedule = schedules[i];
            if (!schedule.IsDue(now))
                continue;

            hasDueSchedule = true;
            schedule.Advance(now);
        }

        if (hasDueSchedule)
            RunScheduledCleanup();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        for (var i = 0; i < schedules.Count; i++)
            schedules[i].MarkDue(now);

        while (!cancellationToken.IsCancellationRequested)
        {
            now = timeProvider.GetUtcNow();
            RunDueScheduledCleanup(now);

            var delay = GetDelayUntilNextSchedule(now);
            if (delay <= TimeSpan.Zero)
                continue;

            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan GetDelayUntilNextSchedule(DateTimeOffset now)
    {
        var nextDue = schedules
            .Select(x => x.NextDue)
            .DefaultIfEmpty(now)
            .Min();

        return nextDue <= now ? TimeSpan.Zero : nextDue - now;
    }

    public void Dispose()
    {
        Stop();
    }

    internal static TimeSpan ConvertCleanupIntervalToTimeSpan(CacheCleanupType type, long amount)
        => type switch
        {
            CacheCleanupType.Seconds => TimeSpan.FromSeconds(amount),
            CacheCleanupType.Minutes => TimeSpan.FromMinutes(amount),
            CacheCleanupType.Hours => TimeSpan.FromHours(amount),
            CacheCleanupType.Days => TimeSpan.FromDays(amount),
            _ => throw new NotImplementedException($"CacheCleanupType '{type}' is not implemented.")
        };

    private sealed class ScheduledCleanupInterval(TimeSpan interval)
    {
        public TimeSpan Interval { get; } = interval;
        public DateTimeOffset NextDue { get; private set; }

        public bool IsDue(DateTimeOffset now) => NextDue <= now;
        public void MarkDue(DateTimeOffset now) => NextDue = now;
        public void Advance(DateTimeOffset now) => NextDue = now + Interval;
    }
}

internal readonly record struct CacheCleanupPassResult(
    string Reason,
    string Trigger,
    string Basis,
    int RowsRemoved,
    long EstimatedBytesBefore,
    long EstimatedBytesAfter,
    long? TargetEstimatedCacheBytes);
