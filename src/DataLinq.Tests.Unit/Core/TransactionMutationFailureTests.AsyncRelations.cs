using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_WaiterCancellationDoesNotCancelOwner(bool reference)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var access = fixture.SetRows(paused: true);
        var holder = fixture.Holder();
        var owner = holder.Read();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var waiter = holder.Read(cancellation.Token);
        try
        {
            await Assert.That(waiter.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(await AsyncEnumerationFailureOf(() => waiter)).IsTypeOf<OperationCanceledException>();
            await Assert.That(owner.IsCompleted).IsFalse();
            await Assert.That(fixture.Dispatches).IsEqualTo(1);
        }
        finally { access.Dispatch.Release(); }
        await Assert.That(await owner).IsEquivalentTo(new[] { 1 });
        await Assert.That(await holder.Read()).IsEquivalentTo(new[] { 1 });
        await Assert.That(fixture.Dispatches).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRelation_FailedOwnerReleasesCapturedWaiterForItsOwnAttempt(bool reference, bool cancel)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var first = fixture.SetRows(paused: true);
        var holder = fixture.Holder();
        using var ownerToken = new CancellationTokenSource();
        using var waiterToken = new CancellationTokenSource();
        var owner = holder.Read(ownerToken.Token);
        await first.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var second = fixture.SetRows(paused: true);
        var waiter = holder.Read(waiterToken.Token);
        // The waiting call must keep its already-bound invocation policy.
        fixture.Factory.CreateAccess = _ => throw new Exception("late replacement must not bind");
        var expected = new InvalidOperationException("owner failed");
        if (cancel) ownerToken.Cancel(); else first.Dispatch.Fail(expected);
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => owner);
            if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
            else await Assert.That(failure).IsSameReferenceAs(expected);
            await second.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(second.Dispatch.ObservedToken).IsEqualTo(waiterToken.Token);
            await Assert.That(waiter.IsCompleted).IsFalse();
        }
        finally { first.Dispatch.Release(); second.Dispatch.Release(); }
        await Assert.That(await waiter).IsEquivalentTo(new[] { 1 });
        await Assert.That(fixture.Dispatches).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_TransactionOverlapPrecedesWaitingAndPreCancellation(bool reference)
    {
        using var fixture = new AsyncRelationFixture(reference);
        using var transaction = fixture.Provider.StartTransaction();
        var access = fixture.SetRows(paused: true);
        var holder = fixture.Holder(transaction);
        var owner = holder.Read();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(await AsyncEnumerationFailureOf(() => holder.Read(new(true)))).IsTypeOf<InvalidOperationException>();
            _ = Capture<InvalidOperationException>(() => holder.ReadSync());
            await Assert.That(fixture.Factory.Inputs.Count).IsEqualTo(1);
        }
        finally { access.Dispatch.Release(); }
        await owner;
        using (transaction.ExecutionGate.Enter("competing operation"))
            await Assert.That(await AsyncEnumerationFailureOf(() => holder.Read())).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_AsyncOwnerAndSynchronousWaiterShareOneLoad(bool reference)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var access = fixture.SetRows(paused: true);
        var holder = fixture.Holder();
        var owner = holder.Read();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = Task.Factory.StartNew(() => { started.SetResult(); return holder.ReadSync(); },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(waiter.IsCompleted).IsFalse();
        }
        finally { access.Dispatch.Release(); }
        await Assert.That(await waiter.WaitAsync(TimeSpan.FromSeconds(10))).IsEquivalentTo(await owner);
        await Assert.That(fixture.Dispatches).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_SynchronousOwnerAndAsyncWaiterShareOneLoad(bool reference)
    {
        using var fixture = new AsyncRelationFixture(reference);
        fixture.SetRows();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new RelationTestRow(fixture.Cache.Table, fixture.Row()))
        {
            OnRead = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); }
        };
        var holder = fixture.Holder();
        var owner = Task.Factory.StartNew(holder.ReadSync, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<int[]>? waiter = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            waiter = holder.Read();
            await Assert.That(waiter.IsCompleted).IsFalse();
            await Assert.That(fixture.Dispatches).IsEqualTo(0);
        }
        finally { release.Set(); }
        await Assert.That(await waiter!).IsEquivalentTo(await owner.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(1);
        await Assert.That(fixture.Dispatches).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRelation_ValidationPrecedesCancellationEvenWithWarmHolder(bool reference, bool warm)
    {
        using var fixture = new AsyncRelationFixture(reference);
        fixture.SetRows();
        var holder = fixture.Holder();
        if (warm) await holder.Read();
        fixture.Scenario.AsyncSqlReaders = null;
        await Assert.That(await AsyncEnumerationFailureOf(() => holder.Read(new(true)))).IsTypeOf<NotSupportedException>();
        await Assert.That(fixture.Dispatches).IsEqualTo(warm ? 1 : 0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRelation_ClearDuringCleanupReturnsReadWithoutPublishingOrReloading(bool reference, bool clearTable)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var access = fixture.SetRows(cleanupPaused: true);
        var reader = (ControlledRowDataReader)access.ReaderOverride!;
        var holder = fixture.Holder();
        var pending = holder.Read();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (clearTable) fixture.Cache.ClearCache(); else holder.Clear();
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { reader.Cleanup.Release(); }
        await Assert.That(await pending).IsEquivalentTo(new[] { 1 });
        await Assert.That(fixture.Dispatches).IsEqualTo(1);
        // Clearing only the holder may retain complete row/index entries, but the
        // invalidated pending load must not have installed a holder snapshot.
        var loadsBefore = fixture.RelationLoads;
        fixture.SetRows(empty: true);
        await Assert.That(await holder.Read()).IsEquivalentTo(clearTable ? Array.Empty<int>() : [1]);
        await Assert.That(fixture.RelationLoads - loadsBefore).IsEqualTo(1L);
        await Assert.That(fixture.Dispatches).IsEqualTo(clearTable ? 2 : 1);
        await holder.Read();
        await Assert.That(fixture.RelationLoads - loadsBefore).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_OlderLoadCannotOverwriteIndependentNewPublication(bool reference)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var first = fixture.SetRows(cleanupPaused: true);
        var old = fixture.Holder();
        var pending = old.Read();
        var reader = (ControlledRowDataReader)first.ReaderOverride!;
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Cache.ClearCache();
        fixture.SetRows(empty: true);
        var fresh = fixture.Holder();
        try { await Assert.That(await fresh.Read()).IsEmpty(); }
        finally { reader.Cleanup.Release(); }
        await Assert.That(await pending).IsEquivalentTo(new[] { 1 });
        fixture.SetRows(empty: true);
        await Assert.That(await old.Read()).IsEmpty();
        await Assert.That(await fresh.Read()).IsEmpty();
        // References do not cache negative rows globally. Collections reuse the fresh complete index.
        await Assert.That(fixture.Dispatches).IsEqualTo(reference ? 3 : 2);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRelation_FailedCleanupOrCanceledLoadNeverCachesAbsence(bool reference, bool cancel)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var access = fixture.SetRows(empty: true, cleanupPaused: true);
        var reader = (ControlledRowDataReader)access.ReaderOverride!;
        var holder = fixture.Holder();
        using var cancellation = new CancellationTokenSource();
        var pending = holder.Read(cancellation.Token);
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new InvalidOperationException("cleanup");
        if (cancel) { cancellation.Cancel(); reader.Cleanup.Release(); } else reader.Cleanup.Fail(expected);
        var failure = await AsyncEnumerationFailureOf(() => pending);
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        fixture.SetRows();
        await Assert.That(await holder.Read()).IsEquivalentTo(new[] { 1 });
        await Assert.That(fixture.Dispatches).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_TransactionMembershipAndTerminalFallbackStayIsolated(bool commit)
    {
        using var fixture = new AsyncRelationFixture();
        using var first = fixture.Provider.StartTransaction();
        using var second = fixture.Provider.StartTransaction();
        fixture.SetRows();
        var firstHolder = fixture.Holder(first);
        await Assert.That(await firstHolder.Read()).IsEquivalentTo(new[] { 1 });
        fixture.SetRows(empty: true);
        await Assert.That(await fixture.Holder(second).Read()).IsEmpty();
        await Assert.That(await fixture.Holder().Read()).IsEmpty();
        await Assert.That(await firstHolder.Read()).IsEquivalentTo(new[] { 1 });
        if (commit) first.Commit(); else first.Rollback();
        await Assert.That(await firstHolder.Read()).IsEmpty();
        await Assert.That(fixture.Dispatches).IsEqualTo(3);
    }

    [Test]
    public async Task AsyncRelation_RequiredReferenceUsesAwaitedResultAndDoesNotPerformSecondLoad()
    {
        using var fixture = new AsyncRelationFixture(reference: true);
        using var transaction = fixture.Provider.StartTransaction();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationChild)).Model.RelationProperties[nameof(AsyncRelationChild.Parent)];
        var reference = new ImmutableForeignKey<AsyncRelationParent, int>(1, transaction, property);
        fixture.SetRows(empty: true);
        var error = await AsyncEnumerationFailureOf(() => reference.GetRequiredValueAsyncCore());
        await Assert.That(error.Message).IsEqualTo("Required relation 'AsyncRelationChild.Parent' did not resolve to a target row.");
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        fixture.SetRows();
        await Assert.That((await reference.GetRequiredValueAsyncCore()).Id).IsEqualTo(1);
        await Assert.That(fixture.Dispatches).IsEqualTo(2);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncRelation_CollectionAndKeyedViewShareCompletedSnapshot()
    {
        using var fixture = new AsyncRelationFixture();
        fixture.SetRows();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationParent)).Model.RelationProperties[nameof(AsyncRelationParent.Children)];
        var relation = new ImmutableRelation<AsyncRelationChild, int>(1, fixture.Provider.ReadOnlyAccess, property);
        var values = await relation.GetValuesAsyncCore();
        var keyed = await relation.GetInstancesAsyncCore();
        await Assert.That(keyed[DataLinqKey.FromValue(1)]).IsSameReferenceAs(values[0]);
        await Assert.That(fixture.Dispatches).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_IndependentHoldersAndDatabasesDoNotShareLoadSlots(bool separateDatabase)
    {
        using var first = new AsyncRelationFixture();
        using var second = new AsyncRelationFixture();
        var blocked = first.SetRows(paused: true);
        var pending = first.Holder().Read();
        await blocked.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var other = separateDatabase ? second : first;
        other.SetRows(empty: true);
        try
        {
            await Assert.That(await other.Holder().Read().WaitAsync(TimeSpan.FromSeconds(10))).IsEmpty();
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { blocked.Dispatch.Release(); }
        await pending;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_PartialReadCannotPublishMembershipOrDiscardExistingValidRows(bool cancel)
    {
        using var fixture = new AsyncRelationFixture();
        fixture.SetRows();
        var key = DataLinqKey.FromValue(1);
        var valid = await fixture.Cache.GetCanonicalRowAsyncCore(key, fixture.Provider.ReadOnlyAccess);
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("later row failed");
        var reader = new ControlledRowDataReader([2, 1], [3, 1])
        {
            Advancing = count => { if (count == 2) { if (cancel) cancellation.Cancel(); else throw expected; } }
        };
        fixture.Factory.CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead };
        var holder = fixture.Holder();
        var failure = await AsyncEnumerationFailureOf(() => holder.Read(cancellation.Token));
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(await fixture.Cache.GetCanonicalRowAsyncCore(key, fixture.Provider.ReadOnlyAccess)).IsSameReferenceAs(valid);
        fixture.SetRows(empty: true);
        await Assert.That(await holder.Read()).IsEmpty();
        await Assert.That(fixture.Dispatches).IsEqualTo(3);
        await Assert.That(fixture.Cache.RowCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_HelperDrainsRelationCleanupAndPreservesFailure(bool failCleanup)
    {
        using var fixture = new AsyncRelationFixture();
        var transaction = fixture.Provider.StartTransaction();
        fixture.Scenario.AsyncCompletion = new();
        var access = fixture.SetRows(cleanupPaused: true);
        var reader = (ControlledRowDataReader)access.ReaderOverride!;
        Task<int[]>? pending = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            pending = fixture.Holder(transaction).Read();
            return Task.FromResult(17);
        }, new());
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(helper.IsCompleted).IsFalse();
        var expected = new InvalidOperationException("cleanup failed");
        if (failCleanup) reader.Cleanup.Fail(expected); else reader.Cleanup.Release();
        if (failCleanup)
        {
            await Assert.That(await AsyncEnumerationFailureOf(() => pending!)).IsSameReferenceAs(expected);
            await Assert.That(ExecutionFailureContexts.Get(expected)!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
        }
        else await pending!;
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncRelation_IndexHitLoadsMissingRowsWithoutRepeatingMembershipQuery()
    {
        using var fixture = new AsyncRelationFixture();
        fixture.SetRows();
        await fixture.Holder().Read();
        fixture.Cache.ClearRows(); // Keeps the complete index membership, drops model rows.
        fixture.SetRows();
        var holder = fixture.Holder();
        await Assert.That(await holder.Read()).IsEquivalentTo(new[] { 1 });
        await Assert.That(fixture.Dispatches).IsEqualTo(2);
        await Assert.That(fixture.Factory.Inputs[^1].Text).Contains("\"id\" = @w0");
        await Assert.That(fixture.Cache.RowCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, "one")]
    [Arguments(true, "one")]
    [Arguments(false, "empty")]
    [Arguments(true, "empty")]
    [Arguments(false, "multiple")]
    [Arguments(true, "multiple")]
    public async Task AsyncRelation_ProviderMatchedReferencesPreserveComparisonAndCardinality(bool composite, string outcome)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncRelationMatchDb>(scenario);
        using var transaction = provider.StartTransaction();
        object?[][] rows = outcome switch { "empty" => [], "multiple" => [[5, "MiXeD", 7], [6, "MiXeD", 7]], _ => [[5, "MiXeD", 7]] };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(rows), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var property = provider.Metadata.GetTableModel(typeof(AsyncRelationMatchChild)).Model.RelationProperties[
            composite ? nameof(AsyncRelationMatchChild.CompositeParent) : nameof(AsyncRelationMatchChild.Parent)];
        var key = composite ? DataLinqKey.FromValues(["mixed", 7]) : DataLinqKey.FromValue("mixed");
        var reference = new ImmutableForeignKey<AsyncRelationMatchParent>(key, transaction, property);
        if (outcome == "multiple")
        {
            await Assert.That(await AsyncEnumerationFailureOf(() => reference.GetValueAsyncCore())).IsTypeOf<InvalidOperationException>();
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
            await Assert.That(transaction.AsyncFailureContext.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
            await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
            factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([5, "MiXeD", 7]), FailureEvidence = TrustedScalarRead };
            await Assert.That((await reference.GetValueAsyncCore())!.Id).IsEqualTo(5);
        }
        else
        {
            var result = await reference.GetValueAsyncCore();
            await Assert.That(result?.Id).IsEqualTo(outcome == "empty" ? (int?)null : 5);
            await Assert.That(await reference.GetValueAsyncCore()).IsSameReferenceAs(result);
        }
        await Assert.That(factory.Accesses.Sum(access => access.Calls.Count(call => call == "dispatch:Reader"))).IsEqualTo(outcome == "multiple" ? 2 : 1);
        await Assert.That(factory.Inputs[0].ToSql().Parameters.Select(parameter => parameter.Value).ToArray())
            .IsEquivalentTo(composite ? new object?[] { "mixed", 7 } : ["mixed"]);
        await Assert.That(scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncRelation_NullCompositeReferenceDoesNotRequireIoAndRequiredStillThrows()
    {
        using var provider = new CapturedReadProvider<AsyncRelationMatchDb>(new());
        var property = provider.Metadata.GetTableModel(typeof(AsyncRelationMatchChild)).Model.RelationProperties[nameof(AsyncRelationMatchChild.CompositeParent)];
        var reference = new ImmutableForeignKey<AsyncRelationMatchParent>(DataLinqKey.FromValues([null, 7]), provider.ReadOnlyAccess, property);
        await Assert.That(await reference.GetValueAsyncCore()).IsNull();
        await Assert.That(await AsyncEnumerationFailureOf(() => reference.GetValueAsyncCore(new(true)))).IsTypeOf<OperationCanceledException>();
        await Assert.That((await AsyncEnumerationFailureOf(() => reference.GetRequiredValueAsyncCore())).Message)
            .Contains("AsyncRelationMatchChild.CompositeParent");
    }

    [Test]
    public async Task AsyncRelation_InvalidPrimaryKeyIsRejectedBeforePreCancellation()
    {
        using var fixture = new AsyncRelationFixture(reference: true);
        fixture.SetRows();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationChild)).Model.RelationProperties[nameof(AsyncRelationChild.Parent)];
        var reference = new ImmutableForeignKey<AsyncRelationParent, string>("not-an-integer", fixture.Provider.ReadOnlyAccess, property);
        await Assert.That(await AsyncEnumerationFailureOf(() => reference.GetValueAsyncCore(new(true)))).IsTypeOf<ArgumentException>();
        await Assert.That(fixture.Factory.Inputs).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_InvalidNeutralIndexResultCannotPublishCompleteMembership(bool duplicate)
    {
        using var fixture = new AsyncRelationFixture();
        fixture.Factory.CreateAccess = _ => new()
        {
            ReaderOverride = new ControlledRowDataReader(duplicate ? [[1, 1], [1, 1]] : [[1, "invalid provider value"]]),
            FailureEvidence = TrustedScalarRead
        };
        var holder = fixture.Holder();
        _ = await AsyncEnumerationFailureOf(() => holder.Read());
        await Assert.That(fixture.Cache.RowCount).IsEqualTo(0);
        fixture.SetRows(empty: true);
        await Assert.That(await holder.Read()).IsEmpty();
        await Assert.That(fixture.Dispatches).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRelation_ProviderMatchedCollectionBuildsConsistentKeyedView(bool duplicate)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncRelationMatchDb>(scenario);
        using var transaction = provider.StartTransaction();
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new()
            {
                ReaderOverride = new ControlledRowDataReader(duplicate ? [[5, "MiXeD", 7], [5, "MiXeD", 7]] : [[5, "MiXeD", 7]]),
                FailureEvidence = TrustedScalarRead
            }
        };
        scenario.AsyncSqlReaders = factory;
        var property = provider.Metadata.GetTableModel(typeof(AsyncRelationMatchParent)).Model.RelationProperties[nameof(AsyncRelationMatchParent.CompositeChildren)];
        var relation = new ImmutableRelation<AsyncRelationMatchChild>(DataLinqKey.FromValues(["mixed", 7]), transaction, property);
        if (duplicate)
        {
            _ = await AsyncEnumerationFailureOf(() => relation.GetInstancesAsyncCore());
            await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
            factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([5, "MiXeD", 7]), FailureEvidence = TrustedScalarRead };
        }
        var keyed = await relation.GetInstancesAsyncCore();
        await Assert.That(keyed.Count).IsEqualTo(1);
        await Assert.That((await relation.GetValuesAsyncCore())[0]).IsSameReferenceAs(keyed[DataLinqKey.FromValue(5)]);
        await Assert.That(factory.Accesses.Sum(access => access.Calls.Count(call => call == "dispatch:Reader"))).IsEqualTo(duplicate ? 2 : 1);
    }

    private sealed class AsyncRelationFixture : IDisposable
    {
        internal readonly ScriptedMutationScenario Scenario = new();
        internal readonly CapturedReadProvider<AsyncRelationDb> Provider;
        internal readonly ControlledSqlReaderFactory Factory = new();
        internal readonly TableCache Cache;
        private readonly bool reference;
        internal AsyncRelationFixture(bool reference = false)
        {
            this.reference = reference;
            Provider = new(Scenario);
            Scenario.AsyncSqlReaders = Factory;
            Cache = Provider.GetTableCache(Provider.Metadata.GetTableModel(reference ? typeof(AsyncRelationParent) : typeof(AsyncRelationChild)).Table);
            // These cases control invalidation explicitly; background eviction would
            // make cache-identity and dispatch-count assertions nondeterministic.
            Provider.State.Cache.CleanupScheduler?.Stop();
        }
        internal int Dispatches => Factory.Accesses.Distinct().Sum(access => access.Calls.Count(call => call == "dispatch:Reader"));
        internal long RelationLoads
        {
            get
            {
                var relations = DataLinqMetrics.Snapshot().Providers.Single(provider => provider.ProviderInstanceId == Provider.TelemetryInstanceId)
                    .Tables.Single(table => table.TableName == Cache.Table.DbName).Relations;
                return reference ? relations.ReferenceLoads : relations.CollectionLoads;
            }
        }
        internal object?[] Row() => reference ? [1] : [1, 1];
        internal ControlledAsyncDatabaseAccess SetRows(bool paused = false, bool empty = false, bool cleanupPaused = false)
        {
            var reader = new ControlledRowDataReader(empty ? [] : [Row()]) { Cleanup = new(cleanupPaused) };
            var access = new ControlledAsyncDatabaseAccess(new(paused)) { ReaderOverride = reader, FailureEvidence = TrustedScalarRead };
            Factory.CreateAccess = _ => access;
            return access;
        }
        internal RelationTestHolder Holder(IDataSourceAccess? source = null)
        {
            source ??= Provider.ReadOnlyAccess;
            if (reference)
            {
                var property = Provider.Metadata.GetTableModel(typeof(AsyncRelationChild)).Model.RelationProperties[nameof(AsyncRelationChild.Parent)];
                var holder = new ImmutableForeignKey<AsyncRelationParent, int>(1, source, property);
                return new(async token => (await holder.GetValueAsyncCore(token)) is { } row ? [row.Id] : [],
                    () => holder.Value is { } row ? [row.Id] : [], holder.Clear);
            }
            else
            {
                var property = Provider.Metadata.GetTableModel(typeof(AsyncRelationParent)).Model.RelationProperties[nameof(AsyncRelationParent.Children)];
                var holder = new ImmutableRelation<AsyncRelationChild, int>(1, source, property);
                return new(async token => (await holder.GetValuesAsyncCore(token)).Select(row => row.Id).ToArray(),
                    () => holder.Values.Select(row => row.Id).ToArray(), holder.Clear);
            }
        }
        public void Dispose() => Provider.Dispose();
    }

    private sealed record RelationTestHolder(Func<CancellationToken, Task<int[]>> Load, Func<int[]> ReadSync, Action Clear)
    {
        internal Task<int[]> Read(CancellationToken token = default) => Load(token);
    }

    private sealed class RelationTestRow(TableDefinition table, object?[] values) : IRowData
    {
        public TableDefinition Table => table;
        public object? this[ColumnDefinition column] => values[column.Index];
        public object? this[int index] => values[index];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int index) => this[index];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) => columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() => GetColumnAndValues(table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns)
            => columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, this[column]));
    }
}

[Database("async_relations"), UseCache, IndexCache(IndexCacheType.All)]
public sealed partial class AsyncRelationDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<AsyncRelationParent> Parents { get; } = new(source);
    public DbRead<AsyncRelationChild> Children { get; } = new(source);
}

[Table("async_parents")]
public abstract partial class AsyncRelationParent(IRowData rowData, IDataSourceAccess source)
    : Immutable<AsyncRelationParent, AsyncRelationDb>(rowData, source), ITableModel<AsyncRelationDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [Relation("async_children", "parent_id", "FK_async_child")] public abstract IImmutableRelation<AsyncRelationChild> Children { get; }
}

[Table("async_children")]
public abstract partial class AsyncRelationChild(IRowData rowData, IDataSourceAccess source)
    : Immutable<AsyncRelationChild, AsyncRelationDb>(rowData, source), ITableModel<AsyncRelationDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [ForeignKey("async_parents", "id", "FK_async_child"), Column("parent_id")] public abstract int ParentId { get; }
    [Relation("async_parents", "id", "FK_async_child")] public abstract AsyncRelationParent Parent { get; }
}

[Database("async_relation_matches"), UseCache, IndexCache(IndexCacheType.All)]
public sealed partial class AsyncRelationMatchDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<AsyncRelationMatchParent> Parents { get; } = new(source);
    public DbRead<AsyncRelationMatchChild> Children { get; } = new(source);
}

[Table("async_match_parents"), Index("UX_match_code", IndexCharacteristic.Unique, "code"),
 Index("UX_match_composite", IndexCharacteristic.Unique, "code", "tenant")]
public abstract partial class AsyncRelationMatchParent(IRowData row, IDataSourceAccess source)
    : Immutable<AsyncRelationMatchParent, AsyncRelationMatchDb>(row, source), ITableModel<AsyncRelationMatchDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [Column("code")] public abstract string Code { get; }
    [Column("tenant")] public abstract int Tenant { get; }
    [Relation("async_match_children", "code", "FK_match_code")] public abstract IImmutableRelation<AsyncRelationMatchChild> Children { get; }
    [Relation("async_match_children", new[] { "code", "tenant" }, "FK_match_composite")] public abstract IImmutableRelation<AsyncRelationMatchChild> CompositeChildren { get; }
}

[Table("async_match_children")]
public abstract partial class AsyncRelationMatchChild(IRowData row, IDataSourceAccess source)
    : Immutable<AsyncRelationMatchChild, AsyncRelationMatchDb>(row, source), ITableModel<AsyncRelationMatchDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [ForeignKey("async_match_parents", "code", "FK_match_code"),
     ForeignKey("async_match_parents", "code", "FK_match_composite"), Column("code")] public abstract string Code { get; }
    [ForeignKey("async_match_parents", "tenant", "FK_match_composite"), Column("tenant")] public abstract int Tenant { get; }
    [Relation("async_match_parents", "code", "FK_match_code")] public abstract AsyncRelationMatchParent Parent { get; }
    [Relation("async_match_parents", new[] { "code", "tenant" }, "FK_match_composite")] public abstract AsyncRelationMatchParent CompositeParent { get; }
}
