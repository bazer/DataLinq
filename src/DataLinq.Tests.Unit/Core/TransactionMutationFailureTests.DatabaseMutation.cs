using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("save-new")]
    [Arguments("save-existing")]
    [Arguments("delete")]
    public async Task DatabaseMutation_InputStaysReservedThroughCommitAndBothCleanups(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new()
        {
            Commit = new(paused: true), TransactionCleanup = new(paused: true), ConnectionCleanup = new(paused: true)
        };
        var mutable = kind is "insert" or "save-new" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        if (mutable.IsNew()) mutable["Id"] = 1;
        mutable["Value"] = "submitted";
        var factory = EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        Task<TransactionMutationGuardRow>? result = null;
        Task work = kind switch
        {
            "delete" => fixture.Database.DeleteAsyncCore(mutable),
            "insert" => result = fixture.Database.InsertAsyncCore(mutable),
            "update" => result = fixture.Database.UpdateAsyncCore(mutable),
            _ => result = fixture.Database.SaveAsyncCore(mutable)
        };
        try
        {
            await completion.Commit.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            AssertReserved();
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
            completion.Commit.Release();
            await completion.TransactionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            AssertReserved();
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
            completion.TransactionCleanup.Release();
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            AssertReserved();
            using var competing = fixture.Database.Transaction();
            _ = Capture<InvalidOperationException>(() => competing.Delete(mutable));
        }
        finally
        {
            completion.Commit.Release(); completion.TransactionCleanup.Release(); completion.ConnectionCleanup.Release();
            await work;
        }
        if (result is not null) await Assert.That((await result).Value).IsEqualTo("stored");
        else await Assert.That(mutable.IsDeleted()).IsTrue();
        mutable["Value"] = "released";
        await Assert.That(completion.Calls.SequenceEqual(["commit", "dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);

        void AssertReserved()
        {
            if (work.IsCompleted) throw new Exception("Helper returned before its lifetime finished.");
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "conflict");
            _ = Capture<InvalidOperationException>(mutable.Reset);
            _ = Capture<InvalidOperationException>(() => mutable.Reset(fixture.CreateImmutable(1, "replace")));
            _ = Capture<InvalidOperationException>(mutable.SetDeleted);
        }
    }

    [Test]
    [Arguments("null")]
    [Arguments("readonly")]
    [Arguments("binding")]
    [Arguments("completion")]
    [Arguments("canceled")]
    public async Task DatabaseMutation_PreparationFailureCleansUpWithoutWritingOrLosingPrimary(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var completion = fixture.Scenario.AsyncCompletion = new()
        {
            InitializationState = TransactionInitializationState.Unused,
            ConnectionCleanup = new(paused: true)
        };
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var expected = new Exception(phase);
        var cleanup = new Exception("connection cleanup");
        var factory = EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        if (phase == "binding") factory.ConfigureCommand = command => command.ValidationFailure = expected;
        if (phase == "completion") completion.ValidationFailure = expected;
        var work = fixture.Database.UpdateAsyncCore(phase == "null" ? null! : mutable,
            phase == "readonly" ? TransactionType.ReadOnly : TransactionType.ReadAndWrite, cancellation.Token);
        try
        {
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(work.IsCompleted).IsFalse();
            if (phase is "binding" or "completion" or "canceled")
                _ = Capture<InvalidOperationException>(() => mutable["Value"] = "conflict");
        }
        finally { completion.ConnectionCleanup.Fail(cleanup); }
        var error = await AsyncEnumerationFailureOf(() => work);
        if (phase is "binding" or "completion") await Assert.That(error).IsSameReferenceAs(expected);
        else if (phase == "canceled") await Assert.That(error).IsTypeOf<OperationCanceledException>();
        else await Assert.That(error is not OperationCanceledException).IsTrue();
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(completion.Calls.SequenceEqual(["dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(factory.Accesses.All(access => access.Calls.IsEmpty)).IsTrue();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable["Value"]).IsEqualTo("submitted");
        mutable["Value"] = "released";
        await Assert.That(fixture.Scenario.CreatedTransactionTypes.Single()).IsEqualTo(
            phase == "readonly" ? TransactionType.ReadOnly : TransactionType.ReadAndWrite);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(0);
    }

    [Test]
    public async Task DatabaseMutation_CancellationRetainsInputThroughIndependentRecoveryAndCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var cancellation = new CancellationTokenSource();
        var completion = fixture.Scenario.AsyncCompletion = new()
        {
            Rollback = new(paused: true), ConnectionCleanup = new(paused: true)
        };
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
        {
            FailureEvidence = new(ExecutionFailureCause.Cancellation, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true)
        };
        EnableAsyncMutations(fixture, access, [1, "stored"]);
        var work = fixture.Database.UpdateAsyncCore(mutable, token: cancellation.Token);
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await completion.Rollback.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(completion.RollbackToken == cancellation.Token).IsFalse();
            await Assert.That(completion.RollbackToken.IsCancellationRequested).IsFalse();
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "conflict");
            await Assert.That(work.IsCompleted).IsFalse();
            completion.Rollback.Release();
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(mutable.Reset);
            await Assert.That(completion.ConnectionCleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally { access.Dispatch.Release(); completion.Rollback.Release(); completion.ConnectionCleanup.Release(); }
        var error = await AsyncEnumerationFailureOf(() => work);
        await Assert.That(error).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)error).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        mutable["Value"] = "released but still invalid";
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(completion.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
    }

    [Test]
    [Arguments("commit")]
    [Arguments("transaction-cleanup")]
    [Arguments("connection-cleanup")]
    [Arguments("committed-publication")]
    public async Task DatabaseMutation_FailurePreservesCompletionAndOrderedCleanupEvidence(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var expected = new InjectedMutationException(phase);
        var secondary = new Exception("last cleanup");
        if (phase == "commit") { completion.Commit = new(paused: true); completion.Commit.Fail(expected); }
        if (phase == "transaction-cleanup") { completion.TransactionCleanup = new(paused: true); completion.TransactionCleanup.Fail(expected); }
        if (phase == "committed-publication") fixture.RowCache.SubscribeToChanges(new ThrowingNotification(expected));
        completion.ConnectionCleanup = new(paused: true);
        completion.ConnectionCleanup.Fail(phase == "connection-cleanup" ? expected : secondary);
        var error = await AsyncEnumerationFailureOf(() => fixture.Database.UpdateAsyncCore(mutable));
        if (phase == "committed-publication")
        {
            await Assert.That(error).IsTypeOf<TransactionCommitFinalizationException>();
            await Assert.That(error.InnerException).IsSameReferenceAs(expected);
        }
        else await Assert.That(error).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Completion).IsEqualTo(phase == "commit" ? ExecutionCompletion.Unknown : ExecutionCompletion.Committed);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        if (phase != "connection-cleanup") await Assert.That(context.SecondaryFailures.Last().Exception).IsSameReferenceAs(secondary);
        await Assert.That(completion.Calls.Count(x => x == "rollback")).IsEqualTo(phase == "commit" ? 1 : 0);
        await Assert.That(completion.Calls.Count(x => x == "dispose-transaction")).IsEqualTo(1);
        await Assert.That(completion.Calls.Count(x => x == "dispose-connection")).IsEqualTo(1);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(
            phase is "commit" or "committed-publication" ? MutableBaselineKind.Invalid : MutableBaselineKind.Committed);
        mutable["Value"] = "released";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DatabaseMutation_UnchangedUpdateUsesCurrentLookupAndKeepsHelperReservation(bool warm)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new() { ConnectionCleanup = new(paused: true) };
        var mutable = fixture.CreateExistingMutable(1, "stale");
        if (warm) fixture.PrimeCommittedRow(1, "current");
        var factory = EnableAsyncMutations(fixture, rows: [[1, "current"]]);
        var work = fixture.Database.UpdateAsyncCore(mutable);
        try
        {
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(work.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "conflict");
        }
        finally { completion.ConnectionCleanup.Release(); }
        await Assert.That((await work).Value).IsEqualTo("current");
        await Assert.That(mutable["Value"]).IsEqualTo("stale");
        await Assert.That(factory.Inputs).IsEmpty();
        mutable["Value"] = "released";
    }

    [Test]
    public async Task DatabaseMutation_UsesOwnTransactionWithoutJoiningUnresolvedSource()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new();
        using var source = fixture.Database.Transaction();
        var fromSource = fixture.CreateImmutable(1, "source", source);
        EnableAsyncMutations(fixture);
        var error = await AsyncEnumerationFailureOf(() => fixture.Database.DeleteAsyncCore(fromSource));
        await Assert.That(error).IsTypeOf<MutationGuardException>();
        await Assert.That(source.Changes).IsEmpty();
        await Assert.That(source.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        source.Commit();
        completion.Calls.Clear();
        await fixture.Database.DeleteAsyncCore(fromSource);
        await Assert.That(source.Changes).IsEmpty();
        await Assert.That(fixture.Scenario.CreatedTransactionTypes.Count).IsEqualTo(3);
        await Assert.That(completion.Calls.SequenceEqual(["commit", "dispose-transaction", "dispose-connection"])).IsTrue();
    }

    [Test]
    public async Task DatabaseMutation_GeneratedKeySurvivesLateCancellation()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new();
        using var late = new CancellationTokenSource();
        completion.Committed = late.Cancel;
        var mutable = fixture.CreateNewAutoMutable("submitted");
        EnableAsyncMutations(fixture, new() { ScalarResult = 42L }, [42, "stored"]);
        var result = await fixture.Database.SaveAsyncCore(mutable, token: late.Token);
        await Assert.That(late.IsCancellationRequested).IsTrue();
        await Assert.That(result.Id).IsEqualTo(42);
        await Assert.That(mutable["Id"]).IsEqualTo(42);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(fixture.Scenario.CreatedTransactionTypes.Count).IsEqualTo(1);
        await Assert.That(completion.Calls.SequenceEqual(["commit", "dispose-transaction", "dispose-connection"])).IsTrue();
        mutable["Value"] = "released";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DatabaseMutation_PrimarySurvivesRollbackAndBothCleanupFailures(bool repeatedIdentity)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var primary = new Exception("execution");
        var rollback = repeatedIdentity ? primary : new Exception("rollback");
        var firstCleanup = repeatedIdentity ? primary : new Exception("transaction cleanup");
        var lastCleanup = repeatedIdentity ? primary : new Exception("connection cleanup");
        var completion = fixture.Scenario.AsyncCompletion = new()
        {
            Rollback = new(paused: true), TransactionCleanup = new(paused: true), ConnectionCleanup = new(paused: true)
        };
        completion.Rollback.Fail(rollback);
        completion.TransactionCleanup.Fail(firstCleanup);
        completion.ConnectionCleanup.Fail(lastCleanup);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
        {
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true)
        };
        access.Dispatch.Fail(primary);
        EnableAsyncMutations(fixture, access);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var error = await AsyncEnumerationFailureOf(() => fixture.Database.UpdateAsyncCore(mutable));
        await Assert.That(error).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        if (repeatedIdentity) await Assert.That(context.SecondaryFailures).IsEmpty();
        else await Assert.That(context.SecondaryFailures.Select(x => x.Exception).SequenceEqual([rollback, firstCleanup, lastCleanup])).IsTrue();
        await Assert.That(completion.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
        mutable["Value"] = "released";
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
    }

    [Test]
    public async Task DatabaseMutation_CapturesHydrationPolicyBeforeSuspension()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Scenario.AsyncCompletion = new();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1 };
        EnableAsyncMutations(fixture, access, [1, "captured"]);
        var work = fixture.Database.SaveAsyncCore(mutable);
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Scenario.AsyncSqlReaders = RawFactory(() => throw new Exception("Later policy must not be used."));
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "changed");
        }
        finally { access.Dispatch.Release(); }
        await Assert.That((await work).Value).IsEqualTo("captured");
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
    }
}
