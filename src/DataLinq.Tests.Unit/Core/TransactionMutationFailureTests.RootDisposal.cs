using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_DatabaseAndProviderShareOneCleanupAttempt(bool asynchronous)
    {
        var calls = new List<string>();
        using var provider = new OwnedRootTestProvider(new(), calls);
        var first = new ScriptedDatabase(provider);
        var second = new ScriptedDatabase(provider);
        if (asynchronous) await first.DisposeAsyncCore(); else first.Dispose();
        second.Dispose();
        await second.DisposeAsyncCore();
        provider.Dispose();
        await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
        await Assert.That(calls.SequenceEqual(new[] { asynchronous ? "state:async" : "state:sync", asynchronous ? "resource:async" : "resource:sync" })).IsTrue();
        await Assert.That(provider.State.Cache.CleanupScheduler?.IsRunning ?? false).IsFalse();
        await Assert.That(provider.Lifetime.IsDisposed).IsTrue();
    }

    [Test]
    public async Task RootDisposal_LegacyProviderRejectsAsyncWithoutSynchronousFallback()
    {
        using var provider = new LegacyRootTestProvider();
        var database = new ScriptedDatabase(provider);
        await Assert.That(await AsyncEnumerationFailureOf(() => database.DisposeAsyncCore().AsTask())).IsTypeOf<NotSupportedException>();
        await Assert.That(provider.Disposals).IsEqualTo(0);
        database.Dispose();
        await Assert.That(provider.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_OverlapRejectsWithoutBlockingOrDuplicatingCleanup(bool asynchronousOwner)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoint = new AsyncCheckpoint(paused: true);
        var calls = 0;
        var tail = 0;
        var lifetime = new OwnedRootDisposal(() => [RootCleanupStep.Resource(
            () => { calls++; entered.SetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); },
            async () => { calls++; await checkpoint.ReachAsync(CancellationToken.None); }), RootCleanupStep.Local(() => tail++)]);
        var pending = asynchronousOwner ? lifetime.DisposeAsync().AsTask()
            : Task.Factory.StartNew(lifetime.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await (asynchronousOwner ? checkpoint.Entered : entered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(Capture<InvalidOperationException>(lifetime.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(await AsyncEnumerationFailureOf(() => lifetime.DisposeAsync().AsTask())).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture<ObjectDisposedException>(lifetime.EnsureUsable)).IsTypeOf<ObjectDisposedException>();
            await Assert.That(tail).IsEqualTo(0);
        }
        finally { release.Set(); checkpoint.Release(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await lifetime.DisposeAsync();
        lifetime.Dispose();
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(tail).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RootDisposal_IndependentFailuresKeepOriginalIdentityAndOrderedContext(bool asynchronous, bool sameFailure)
    {
        var first = new InvalidOperationException("first owned resource");
        var second = sameFailure ? first : new InvalidOperationException("second owned resource");
        var calls = new List<int>();
        var lifetime = new OwnedRootDisposal(() => [Resource(1, first), Resource(2, second), RootCleanupStep.Local(() => calls.Add(3))]);
        RootCleanupStep Resource(int index, Exception failure) => RootCleanupStep.Resource(
            () => { calls.Add(index); throw failure; }, () => { calls.Add(index); return ValueTask.FromException(failure); });
        var thrown = asynchronous ? await AsyncEnumerationFailureOf(() => lifetime.DisposeAsync().AsTask()) : Capture<Exception>(lifetime.Dispose);
        await Assert.That(thrown).IsSameReferenceAs(first);
        await Assert.That(calls.SequenceEqual(new[] { 1, 2, 3 })).IsTrue();
        var context = ExecutionFailureContexts.Get(thrown)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(sameFailure ? 0 : 1);
        if (!sameFailure) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(second);
        lifetime.Dispose();
        await lifetime.DisposeAsync();
        await Assert.That(calls.Count).IsEqualTo(3);
    }

    [Test]
    public async Task RootDisposal_UnsupportedResourceIsValidatedBeforeAnyCleanupOrClosure()
    {
        var calls = 0;
        var lifetime = new OwnedRootDisposal(() => [RootCleanupStep.Local(() => calls++), RootCleanupStep.Resource(() => calls++, null)]);
        await Assert.That(await AsyncEnumerationFailureOf(() => lifetime.DisposeAsync().AsTask())).IsTypeOf<NotSupportedException>();
        lifetime.EnsureUsable();
        await Assert.That(calls).IsEqualTo(0);
        lifetime.Dispose();
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task RootDisposal_CapturesResourceListBeforeSuspension()
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        var captured = 0;
        var replacement = 0;
        RootCleanupStep[] steps = [RootCleanupStep.Resource(() => { }, () => new(checkpoint.ReachAsync(CancellationToken.None))), RootCleanupStep.Local(() => captured++)];
        var lifetime = new OwnedRootDisposal(() => steps);
        var pending = lifetime.DisposeAsync();
        await checkpoint.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        steps[1] = RootCleanupStep.Local(() => replacement++);
        checkpoint.Release();
        await pending;
        await Assert.That(captured).IsEqualTo(1);
        await Assert.That(replacement).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_DoesNotCompleteOrDisposeDependentTransaction(bool asynchronous)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new OwnedRootTestProvider(scenario, []);
        using var transaction = provider.StartTransaction();
        if (asynchronous) await provider.DisposeAsyncCore(); else provider.Dispose();
        await Assert.That(scenario.Commits).IsEqualTo(0);
        await Assert.That(scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(scenario.Disposals).IsEqualTo(0);
        // This does not promise that dependent work remains usable. Callers must
        // finish its lifetime before root disposal; no implicit drain is added.
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_CacheWaitsForItsMaintenanceBeforeClearingAndCannotRestart(bool asynchronous)
    {
        using var clock = new PausedMaintenanceClock();
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        var scheduler = cache.CleanupScheduler!;
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var table = cache.TableCaches.Values.First();
        var generation = table.CaptureReadGeneration();
        var pending = asynchronous ? cache.DisposeAsyncCore().AsTask()
            : Task.Factory.StartNew(cache.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await Assert.That(SpinWait.SpinUntil(() => scheduler.IsStopping, TimeSpan.FromSeconds(10))).IsTrue();
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(scheduler.IsRunning).IsTrue();
            await Assert.That(scheduler.BackgroundWorkerCount).IsEqualTo(1);
            await Assert.That(ReferenceEquals(table.CaptureReadGeneration(), generation)).IsTrue();
            await Assert.That(Capture<ObjectDisposedException>(scheduler.Start)).IsTypeOf<ObjectDisposedException>();
            await Assert.That(Capture<InvalidOperationException>(cache.Dispose)).IsTypeOf<InvalidOperationException>();
        }
        finally { clock.Release.Set(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(scheduler.IsRunning).IsFalse();
        await Assert.That(scheduler.IsStopping).IsFalse();
        await Assert.That(scheduler.BackgroundWorkerCount).IsEqualTo(0);
        await Assert.That(ReferenceEquals(table.CaptureReadGeneration(), generation)).IsFalse();
        await Assert.That(Capture<ObjectDisposedException>(scheduler.Start)).IsTypeOf<ObjectDisposedException>();
        await cache.DisposeAsyncCore();
        cache.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_WorkerFailureDoesNotSkipRemainingOwnedCleanup(bool asynchronous)
    {
        using var clock = new PausedMaintenanceClock();
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new InvalidOperationException("maintenance failure");
        clock.AfterRelease = () => throw expected;
        var table = cache.TableCaches.Values.First();
        var generation = table.CaptureReadGeneration();
        var pending = asynchronous ? cache.DisposeAsyncCore().AsTask()
            : Task.Factory.StartNew(cache.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        clock.Release.Set();
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(cache.CleanupScheduler!.IsRunning).IsFalse();
        await Assert.That(ReferenceEquals(table.CaptureReadGeneration(), generation)).IsFalse();
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
    }

    [Test]
    public async Task RootDisposal_MaintenanceCallbackCannotCloseRootOrWaitForItself()
    {
        using var clock = new PausedMaintenanceClock();
        var calls = new List<string>();
        using var provider = new OwnedRootTestProvider(new(), calls);
        var database = new ScriptedDatabase(provider);
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var observed = new TaskCompletionSource<Exception[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.AfterRelease = () => observed.SetResult([Capture<InvalidOperationException>(cache.Dispose),
            Capture<InvalidOperationException>(() => cache.DisposeAsyncCore()),
            Capture<InvalidOperationException>(database.Dispose),
            Capture<InvalidOperationException>(() => database.DisposeAsyncCore())]);
        clock.Release.Set();
        var failures = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(failures.All(failure => failure is InvalidOperationException)).IsTrue();
        provider.Lifetime.EnsureUsable();
        await Assert.That(calls.Count).IsEqualTo(0);
        await database.DisposeAsyncCore();
        await Assert.That(cache.CleanupScheduler!.IsRunning).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_JoinsTemporarySchedulerStopBeforeClearing(bool asynchronous)
    {
        using var clock = new PausedMaintenanceClock();
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        var scheduler = cache.CleanupScheduler!;
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stopping = Task.Factory.StartNew(scheduler.Stop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task disposing = Task.CompletedTask;
        try
        {
            await Assert.That(SpinWait.SpinUntil(() => scheduler.IsStopping, TimeSpan.FromSeconds(10))).IsTrue();
            var generation = cache.TableCaches.Values.First().CaptureReadGeneration();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposing = asynchronous ? cache.DisposeAsyncCore().AsTask() : Task.Factory.StartNew(() =>
            {
                entered.SetResult();
                cache.Dispose();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (!asynchronous) await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Wait for the root to upgrade temporary shutdown to permanent disposal.
            await Assert.That(SpinWait.SpinUntil(() =>
            {
                try { scheduler.Start(); return false; }
                catch (ObjectDisposedException) { return true; }
                catch (InvalidOperationException) { return false; }
            }, TimeSpan.FromSeconds(10))).IsTrue();
            await Assert.That(disposing.IsCompleted).IsFalse();
            await Assert.That(scheduler.IsRunning).IsTrue();
            await Assert.That(ReferenceEquals(generation, cache.TableCaches.Values.First().CaptureReadGeneration())).IsTrue();
        }
        finally { clock.Release.Set(); }
        await Task.WhenAll(stopping, disposing).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(scheduler.IsRunning).IsFalse();
        await Assert.That(Capture<ObjectDisposedException>(scheduler.Start)).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task RootDisposal_TemporaryStopCanRestartButPermanentDisposeCannot()
    {
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], TimeProvider.System, UnsupportedMemoryPressureReader.Instance));
        var scheduler = cache.CleanupScheduler!;
        scheduler.Stop();
        await Assert.That(scheduler.IsRunning).IsFalse();
        scheduler.Start();
        await Assert.That(scheduler.IsRunning).IsTrue();
        scheduler.Restart();
        await Assert.That(scheduler.IsRunning).IsTrue();
        await cache.DisposeAsyncCore();
        await Assert.That(scheduler.IsRunning).IsFalse();
        await Assert.That(Capture<ObjectDisposedException>(scheduler.Restart)).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_LocalCleanupFailuresDoNotSkipOtherTablesOrProviderResource(bool asynchronous)
    {
        var first = new InvalidOperationException("first table notification");
        var second = new InvalidOperationException("second table notification");
        var third = new InvalidOperationException("provider resource cleanup");
        var calls = new List<string>();
        using var provider = new OwnedRootTestProvider(new(), calls, third);
        provider.State.Cache.Dispose();
        // No-worker mode also models the browser's local cache cleanup path.
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration, _ => null);
        var tables = cache.TableCaches.Values.ToArray();
        await Assert.That(tables.Length).IsGreaterThanOrEqualTo(2);
        var generations = tables.Select(table => table.CaptureReadGeneration()).ToArray();
        var firstNotification = new ThrowingNotification(first);
        var secondNotification = new ThrowingNotification(second);
        tables[0].SubscribeToChanges(firstNotification);
        tables[1].SubscribeToChanges(secondNotification);
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => provider.DisposeAsyncCore().AsTask())
            : Capture<Exception>(provider.Dispose);
        await Assert.That(failure).IsSameReferenceAs(first);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(2);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(second);
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(third);
        for (var i = 0; i < tables.Length; i++)
            await Assert.That(ReferenceEquals(generations[i], tables[i].CaptureReadGeneration())).IsFalse();
        await Assert.That(calls.Count).IsEqualTo(2);
        provider.Dispose();
        await provider.DisposeAsyncCore();
        await Assert.That(calls.Count).IsEqualTo(2);
        GC.KeepAlive(firstNotification);
        GC.KeepAlive(secondNotification);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_DoesNotSwallowForeignWorkerCancellation(bool asynchronous)
    {
        using var clock = new PausedMaintenanceClock();
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var expected = new OperationCanceledException(foreign.Token);
        clock.AfterRelease = () => throw expected;
        var pending = asynchronous ? cache.DisposeAsyncCore().AsTask()
            : Task.Factory.StartNew(cache.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try { await Assert.That(SpinWait.SpinUntil(() => cache.CleanupScheduler!.IsStopping, TimeSpan.FromSeconds(10))).IsTrue(); }
        finally { clock.Release.Set(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(cache.CleanupScheduler!.IsRunning).IsFalse();
    }

    [Test]
    public async Task RootDisposal_MaintenanceSelfDisposalIsRejectedAfterAwait()
    {
        var clock = new ControlledMaintenanceClock();
        using var provider = new OwnedRootTestProvider(new(), []);
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        var timer = await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.OnTick = () =>
        {
            Capture<InvalidOperationException>(provider.Dispose);
            Capture<InvalidOperationException>(() => provider.DisposeAsyncCore());
            observed.SetResult();
        };
        timer.Fire();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        provider.Lifetime.EnsureUsable();
        await provider.DisposeAsyncCore();
        await Assert.That(cache.CleanupScheduler!.IsRunning).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposal_CancellationCallbackFailureStillSettlesWorkerAndLocalCleanup(bool asynchronous)
    {
        var clock = new ControlledMaintenanceClock();
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration,
            owner => new CacheCleanupScheduler(owner, [(CacheCleanupType.Seconds, 60)], clock, UnsupportedMemoryPressureReader.Instance));
        var timer = await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new InvalidOperationException("timer cancellation callback");
        var rejected = false;
        timer.OnDispose = () =>
        {
            Capture<InvalidOperationException>(cache.CleanupScheduler!.Stop);
            rejected = true;
            throw expected;
        };
        var table = cache.TableCaches.Values.First();
        var generation = table.CaptureReadGeneration();
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => cache.DisposeAsyncCore().AsTask()) : Capture<Exception>(cache.Dispose);
        await Assert.That(failure is AggregateException aggregate && ReferenceEquals(aggregate.InnerException, expected)).IsTrue();
        await Assert.That(rejected).IsTrue();
        await Assert.That(cache.CleanupScheduler!.IsRunning).IsFalse();
        await Assert.That(cache.CleanupScheduler.IsStopping).IsFalse();
        await Assert.That(ReferenceEquals(generation, table.CaptureReadGeneration())).IsFalse();
    }

    private sealed class ControlledMaintenanceClock : TimeProvider
    {
        internal TaskCompletionSource<ControlledMaintenanceTimer> TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnTick { get; set; }
        public override DateTimeOffset GetUtcNow() { OnTick?.Invoke(); return DateTimeOffset.UtcNow; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledMaintenanceTimer(callback, state);
            TimerCreated.TrySetResult(timer);
            return timer;
        }
    }

    private sealed class ControlledMaintenanceTimer(TimerCallback callback, object? state) : ITimer
    {
        private int disposed;
        internal Action? OnDispose { get; set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) OnDispose?.Invoke(); }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }

    private sealed class OwnedRootTestProvider(ScriptedMutationScenario scenario, List<string> calls, Exception? resourceFailure = null)
        : ScriptedMutationProvider<TransactionMutationGuardDb>(scenario), IAsyncRootDisposal
    {
        private OwnedRootDisposal? lifetime;
        internal OwnedRootDisposal Lifetime => LazyInitializer.EnsureInitialized(ref lifetime, () => new OwnedRootDisposal(() =>
        [
            RootCleanupStep.Resource(() => { calls.Add("state:sync"); State.Dispose(); },
                async () => { calls.Add("state:async"); await State.DisposeAsyncCore(); }, State.ValidateDisposal),
            RootCleanupStep.Resource(() => { calls.Add("resource:sync"); if (resourceFailure is not null) throw resourceFailure; },
                () => { calls.Add("resource:async"); return resourceFailure is null ? default : ValueTask.FromException(resourceFailure); })
        ]));
        public override void Dispose() => Lifetime.Dispose();
        public ValueTask DisposeAsyncCore() => Lifetime.DisposeAsync();
    }

    private sealed class LegacyRootTestProvider() : ScriptedMutationProvider<TransactionMutationGuardDb>(new())
    {
        internal int Disposals { get; private set; }
        public override void Dispose() { Disposals++; base.Dispose(); }
    }

    private sealed class PausedMaintenanceClock : TimeProvider, IDisposable
    {
        private int calls;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal Action? AfterRelease { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Maintenance was not released.");
                AfterRelease?.Invoke();
            }
            return DateTimeOffset.UtcNow;
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }
}
