using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Execution;

namespace DataLinq.Cache;

public sealed class CacheCleanupScheduler : IDisposable
{
    private readonly object lifecycleGate = new();
    private readonly DatabaseCache cache;
    private readonly TimeProvider timeProvider;
    private readonly IMemoryPressureReader memoryPressureReader;
    private readonly CacheCleanupPolicyEvaluator cleanupPolicyEvaluator = new();
    private readonly List<ScheduledCleanupInterval> schedules;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? workerTask;
    private bool stopping;
    private bool disposed;
    private TaskCompletionSource<ExecutionFailures?>? stopCompletion;
    // Self-join detection only, never execution authority. Synchronous callbacks
    // restore this marker before any await, including on subsequent worker ticks.
    [ThreadStatic] private static CacheCleanupScheduler? executingMaintenance;
    private DateTimeOffset? lastPressureCleanupAt;
    private DateTimeOffset nextPressureCheck;

    internal CacheCleanupScheduler(
        DatabaseCache cache,
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)> cleanupIntervals,
        TimeProvider timeProvider,
        IMemoryPressureReader memoryPressureReader)
    {
        this.cache = cache;
        this.timeProvider = timeProvider;
        this.memoryPressureReader = memoryPressureReader;
        schedules = cleanupIntervals
            .Select(x => new ScheduledCleanupInterval(ConvertCleanupIntervalToTimeSpan(x.cleanupType, x.amount)))
            .Where(x => x.Interval > TimeSpan.Zero)
            .OrderBy(x => x.Interval)
            .ToList();
    }

    public int ActiveScheduleCount => schedules.Count;
    public bool IsRunning => Volatile.Read(ref workerTask) is { IsCompleted: false };

    internal int BackgroundWorkerCount => IsRunning ? 1 : 0;
    internal bool IsStopping { get { lock (lifecycleGate) return stopping; } }

    public void Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stopping) throw new InvalidOperationException("Cache maintenance shutdown is in progress.");
            if (!HasWorkToSchedule()) return;
            if (workerTask is { IsCompleted: false })
                return;
            if (workerTask is not null)
                throw new InvalidOperationException("Stop the completed cache maintenance worker before starting another one.");

            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;
            workerTask = Task.Factory.StartNew(
                () => RunLoopAsync(token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        Stop(permanent: false);
    }

    internal void ValidateStop()
    {
        if (ReferenceEquals(executingMaintenance, this))
            throw new InvalidOperationException("Cache maintenance cannot dispose its own owning root or wait for itself.");
    }

    private (CancellationTokenSource Source, Task Task, TaskCompletionSource<ExecutionFailures?> Completion, bool Owner)? BeginStop(bool permanent)
    {
        ValidateStop();
        lock (lifecycleGate)
        {
            // A root can take over a temporary Stop already in progress. Both
            // callers observe the same shutdown; neither abandons the worker.
            if (stopping)
            {
                disposed |= permanent;
                return (cancellationTokenSource!, workerTask!, stopCompletion!, false);
            }
            if (disposed) return null;
            if (permanent) disposed = true;
            if (workerTask is null) return null;
            stopping = true;
            stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return (cancellationTokenSource!, workerTask, stopCompletion, true);
        }
    }

    private ExecutionFailures? Cancel(CancellationTokenSource source)
    {
        var previous = executingMaintenance;
        executingMaintenance = this;
        try { source.Cancel(); return null; }
        catch (Exception failure) { var failures = new ExecutionFailures(); failures.AddCleanup(failure); return failures; }
        finally { executingMaintenance = previous; }
    }

    private void Stop(bool permanent)
    {
        if (BeginStop(permanent) is not { } worker) return;
        if (!worker.Owner)
        {
            OwnedRootDisposal.ThrowFailures(worker.Completion.Task.GetAwaiter().GetResult());
            return;
        }
        var failures = Cancel(worker.Source);
        try
        {
            // Join this root's local maintenance task. This is not a provider
            // sync-over-async execution path and has no abandonment timeout.
            worker.Task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException canceled) when (worker.Source.IsCancellationRequested && canceled.CancellationToken == worker.Source.Token) { }
        catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        FinishStop(worker.Source, ref failures);
        worker.Completion.SetResult(failures);
        OwnedRootDisposal.ThrowFailures(failures);
    }

    internal ValueTask DisposeAsyncCore() => StopAsync(permanent: true);

    private async ValueTask StopAsync(bool permanent)
    {
        if (BeginStop(permanent) is not { } worker) return;
        if (!worker.Owner)
        {
            OwnedRootDisposal.ThrowFailures(await worker.Completion.Task.ConfigureAwait(false));
            return;
        }
        var failures = Cancel(worker.Source);
        try { await worker.Task.ConfigureAwait(false); }
        catch (OperationCanceledException canceled) when (worker.Source.IsCancellationRequested && canceled.CancellationToken == worker.Source.Token) { }
        catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        FinishStop(worker.Source, ref failures);
        worker.Completion.SetResult(failures);
        OwnedRootDisposal.ThrowFailures(failures);
    }

    private void FinishStop(CancellationTokenSource source, ref ExecutionFailures? failures)
    {
        try { source.Dispose(); }
        catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        finally
        {
            lock (lifecycleGate)
            {
                workerTask = null;
                cancellationTokenSource = null;
                stopCompletion = null;
                stopping = false;
            }
        }
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

    internal CacheCleanupPassResult RunMemoryPressureCleanup(DateTimeOffset now)
    {
        var cacheEstimate = cache.GetMemoryEstimate();
        var beforeEstimate = cacheEstimate.EstimatedCacheBytes;
        var decision = cleanupPolicyEvaluator.EvaluateMemoryPressure(
            memoryPressureReader.GetSnapshot(),
            cacheEstimate,
            cache.MemoryPressureCleanupPolicy,
            now,
            lastPressureCleanupAt);

        if (!decision.ShouldClean || !decision.TargetEstimatedCacheBytes.HasValue)
        {
            return new CacheCleanupPassResult(
                decision.Reason,
                decision.Trigger,
                decision.Basis,
                RowsRemoved: 0,
                EstimatedBytesBefore: beforeEstimate,
                EstimatedBytesAfter: beforeEstimate,
                TargetEstimatedCacheBytes: null,
                decision.NoopReason);
        }

        var result = cache.RemoveRowsForMemoryPressure(
            decision.TargetEstimatedCacheBytes.Value,
            decision.MaxRowsToRemove);
        lastPressureCleanupAt = now;
        return result;
    }

    internal CacheCleanupPassResult RunDueMemoryPressureCleanup(DateTimeOffset now)
    {
        if (!cache.MemoryPressureCleanupPolicy.Enabled)
        {
            var estimatedBytes = cache.GetMemoryEstimate().EstimatedCacheBytes;
            return new CacheCleanupPassResult(
                CacheMaintenanceReasons.Unknown,
                CacheMaintenanceTriggers.MemoryPressure,
                CacheMaintenanceBases.EstimatedCacheBytes,
                RowsRemoved: 0,
                EstimatedBytesBefore: estimatedBytes,
                EstimatedBytesAfter: estimatedBytes,
                TargetEstimatedCacheBytes: null,
                NoopReason: "disabled");
        }

        if (nextPressureCheck != default && nextPressureCheck > now)
        {
            var estimatedBytes = cache.GetMemoryEstimate().EstimatedCacheBytes;
            return new CacheCleanupPassResult(
                CacheMaintenanceReasons.Unknown,
                CacheMaintenanceTriggers.MemoryPressure,
                CacheMaintenanceBases.EstimatedCacheBytes,
                RowsRemoved: 0,
                EstimatedBytesBefore: estimatedBytes,
                EstimatedBytesAfter: estimatedBytes,
                TargetEstimatedCacheBytes: null,
                NoopReason: "not_due");
        }

        nextPressureCheck = now + cache.MemoryPressureCleanupPolicy.Normalize().CheckInterval;
        return RunMemoryPressureCleanup(now);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            Task delayTask;
            var previous = executingMaintenance;
            executingMaintenance = this;
            try
            {
                var now = timeProvider.GetUtcNow();
                if (first)
                {
                    foreach (var schedule in schedules) schedule.MarkDue(now);
                    nextPressureCheck = now;
                    first = false;
                }
                RunDueScheduledCleanup(now);
                RunDueMemoryPressureCleanup(now);
                var delay = GetDelayUntilNextWork(now);
                delayTask = delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, timeProvider, cancellationToken);
            }
            finally { executingMaintenance = previous; }
            await delayTask.ConfigureAwait(false);
        }
    }

    private TimeSpan GetDelayUntilNextWork(DateTimeOffset now)
    {
        var nextDue = schedules
            .Select(x => x.NextDue)
            .DefaultIfEmpty(DateTimeOffset.MaxValue)
            .Min();

        if (cache.MemoryPressureCleanupPolicy.Enabled)
            nextDue = nextPressureCheck <= now
                ? now
                : (nextPressureCheck < nextDue ? nextPressureCheck : nextDue);

        if (nextDue == DateTimeOffset.MaxValue)
            return TimeSpan.FromMinutes(1);

        return nextDue <= now ? TimeSpan.Zero : nextDue - now;
    }

    private bool HasWorkToSchedule() => schedules.Count > 0 || cache.MemoryPressureCleanupPolicy.Enabled;

    public void Dispose()
    {
        Stop(permanent: true);
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
    long? TargetEstimatedCacheBytes,
    string NoopReason = "");
