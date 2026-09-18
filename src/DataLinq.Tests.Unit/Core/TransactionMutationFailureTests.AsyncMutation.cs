using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private sealed partial class ScriptedDatabaseTransaction : IAsyncMutationCommandFactory
    {
        public AsyncEagerCommand BindMutation(CapturedSql sql) =>
            (scenario.AsyncMutations ?? throw new NotSupportedException("Scripted asynchronous mutations are not enabled.")).BindMutation(sql);
    }

    private static ControlledMutationCommandFactory EnableAsyncMutations(ScriptedFixture fixture,
        ControlledAsyncDatabaseAccess? access = null, params object?[][] rows)
    {
        var factory = new ControlledMutationCommandFactory();
        if (access is not null) factory.CreateAccess = _ => access;
        fixture.Scenario.AsyncMutations = factory;
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new()
        {
            ReaderOverride = new ControlledRowDataReader(rows) { ColumnNames = ["id", "value"] },
            FailureEvidence = TrustedScalarRead
        });
        return factory;
    }

    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("save-new")]
    [Arguments("save-existing")]
    [Arguments("delete")]
    public async Task AsyncMutation_SuccessHydratesFinalizesAndReservesAcrossDispatch(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = kind is "insert" or "save-new" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        if (mutable.IsNew()) mutable["Id"] = 1;
        mutable["Value"] = "captured";
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1 };
        var factory = EnableAsyncMutations(fixture, access, [1, "hydrated"]);
        Task? work = null;
        Task<TransactionMutationGuardRow>? result = null;
        try
        {
            work = kind switch
            {
                "delete" => transaction.DeleteAsyncCore(mutable),
                "insert" => result = transaction.InsertAsyncCore(mutable),
                "update" => result = transaction.UpdateAsyncCore(mutable),
                _ => result = transaction.SaveAsyncCore(mutable)
            };
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(work.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "conflict");
            _ = Capture<InvalidOperationException>(() => mutable[fixture.RowTable.GetColumnByDbName("value")] = "conflict");
            _ = Capture<InvalidOperationException>(() => mutable.SetValue("Value", "conflict"));
            _ = Capture<InvalidOperationException>(mutable.Reset);
            _ = Capture<InvalidOperationException>(() => mutable.Reset(fixture.CreateImmutable(1, "replace")));
            _ = Capture<InvalidOperationException>(mutable.SetDeleted);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Commit);
            using var other = fixture.Database.Transaction();
            _ = Capture<InvalidOperationException>(() => other.Delete(mutable));
            await Assert.That(await AsyncEnumerationFailureOf(() => other.UpdateAsyncCore(mutable))).IsTypeOf<InvalidOperationException>();
            await Assert.That(mutable["Value"]).IsEqualTo("captured");
        }
        finally { access.Dispatch.Release(); if (work is not null) await work; }
        if (result is not null)
        {
            await Assert.That((await result).Value).IsEqualTo("hydrated");
            await Assert.That(mutable["Value"]).IsEqualTo("hydrated");
            await Assert.That(mutable.HasChanges()).IsFalse();
        }
        else await Assert.That(mutable.IsDeleted()).IsTrue();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Commands.Single().Resource.SyncDisposals).IsEqualTo(0);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        mutable["Value"] = "after completion";
        transaction.Commit();
    }

    [Test]
    public async Task AsyncMutation_GeneratedKeyUsesPrivateReservationAndAuthoritativeBaseline()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateNewAutoMutable("submitted");
        var access = new ControlledAsyncDatabaseAccess { ScalarResult = 42L };
        var factory = EnableAsyncMutations(fixture, access, [42, "stored"]);
        var result = await transaction.InsertAsyncCore(mutable);
        await Assert.That(result.Id).IsEqualTo(42);
        await Assert.That(result.Value).IsEqualTo("stored");
        await Assert.That(mutable["Id"]).IsEqualTo(42);
        await Assert.That(mutable.IsNew()).IsFalse();
        await Assert.That(mutable.HasChanges()).IsFalse();
        await Assert.That(access.Calls.Contains("dispatch:Scalar")).IsTrue();
        await Assert.That(transaction.Changes.Single().PrimaryKeys).IsEqualTo(DataLinqKey.FromValue(42));
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("cancel-before")]
    [Arguments("validation")]
    [Arguments("create")]
    [Arguments("dispatch")]
    [Arguments("command-cleanup")]
    [Arguments("hydrate")]
    public async Task AsyncMutation_FailureSeparatesPreDispatchFromUncertainWrites(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var prior = fixture.CreateExistingMutable(9, "pending");
        transaction.Delete(prior);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "captured";
        var expected = new Exception(phase);
        var access = new ControlledAsyncDatabaseAccess(new(paused: phase == "dispatch")) { NonQueryResult = 1, FailureEvidence = new(Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true) };
        var factory = EnableAsyncMutations(fixture, access, [1, "stored"]);
        factory.ConfigureCommand = command =>
        {
            if (phase == "validation") command.ValidationFailure = expected;
            if (phase == "create") command.Creating = () => throw expected;
            if (phase == "command-cleanup") { command.Resource.Cleanup = new(paused: true); command.Resource.Cleanup.Fail(expected); }
        };
        if (phase == "dispatch") access.Dispatch.Fail(expected);
        if (phase == "hydrate") fixture.Scenario.AsyncSqlReaders = RawFactory(() =>
        {
            var source = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
            source.Dispatch.Fail(expected);
            return source;
        });
        using var cancellation = new CancellationTokenSource();
        if (phase == "cancel-before") cancellation.Cancel();
        var error = await AsyncEnumerationFailureOf(() => transaction.UpdateAsyncCore(mutable, cancellation.Token));
        if (phase == "cancel-before") await Assert.That(error).IsTypeOf<OperationCanceledException>();
        else await Assert.That(error).IsSameReferenceAs(expected);
        var wrote = phase is "dispatch" or "command-cleanup" or "hydrate";
        await Assert.That(transaction.IsPoisoned).IsEqualTo(wrote);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        // Reservation release is separate from retaining an invalid baseline.
        mutable["Value"] = "after failure";
        if (wrote)
        {
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
            await Assert.That(prior.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
            await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(phase == "command-cleanup"
                ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
            _ = Capture<Exception>(transaction.Commit);
        }
        else
        {
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
            _ = transaction.Query();
            transaction.Commit();
        }
    }

    [Test]
    public async Task AsyncMutation_CleanupAndHydrationKeepReservationAndCancellationPoisonsWrittenWork()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var access = new ControlledAsyncDatabaseAccess { NonQueryResult = 1, FailureEvidence = TrustedScalarRead };
        var factory = EnableAsyncMutations(fixture, access, [1, "stored"]);
        factory.ConfigureCommand = command => command.Resource.Cleanup = new(paused: true);
        var reader = new ControlledRowDataReader([1, "stored"]) { Advance = new(paused: true) };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead });
        var work = transaction.UpdateAsyncCore(mutable, cancellation.Token);
        var cleanup = factory.Commands[0].Resource.Cleanup;
        try
        {
            await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "during cleanup");
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            cleanup.Release();
            await reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(mutable.Reset);
            cancellation.Cancel();
            await Assert.That(await AsyncEnumerationFailureOf(() => work)).IsTypeOf<OperationCanceledException>();
        }
        finally { cleanup.Release(); reader.Advance.Release(); }
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        mutable["Value"] = "released";
    }

    [Test]
    [Arguments("duplicate")]
    [Arguments("enumeration")]
    [Arguments("null")]
    public async Task AsyncMutation_BatchCaptureFailureReleasesEveryInputBeforeAnyWrite(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var first = fixture.CreateNewAutoMutable("first");
        var factory = EnableAsyncMutations(fixture);
        var enumerations = 0;
        IEnumerable<Mutable<TransactionMutationGuardAutoRow>> Input()
        {
            enumerations++;
            yield return first;
            if (mode == "enumeration") throw new Exception("input enumeration");
            yield return mode == "duplicate" ? first : null!;
        }
        _ = await AsyncEnumerationFailureOf(() => transaction.InsertAsyncCore(Input()));
        await Assert.That(enumerations).IsEqualTo(1);
        await Assert.That(factory.Accesses).IsEmpty();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        first["Value"] = "released";
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMutation_BatchCapturesMembershipOrderAndRejectsPartialCommit(bool cancelAfterFirst)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var first = new Mutable<TransactionMutationGuardRow>();
        first["Id"] = 1; first["Value"] = "first";
        var second = new Mutable<TransactionMutationGuardRow>();
        second["Id"] = 2; second["Value"] = "second";
        var original = new List<Mutable<TransactionMutationGuardRow>> { first, second };
        var accesses = new List<ControlledAsyncDatabaseAccess>();
        var factory = EnableAsyncMutations(fixture);
        factory.CreateAccess = _ =>
        {
            var access = new ControlledAsyncDatabaseAccess(new(paused: accesses.Count == 0))
            { NonQueryResult = 1, FailureEvidence = TrustedScalarRead };
            accesses.Add(access);
            return access;
        };
        var reads = 0;
        fixture.Scenario.AsyncSqlReaders = RawFactory(() =>
        {
            var id = ++reads;
            return new() { ReaderOverride = new ControlledRowDataReader([id, "stored " + id]), FailureEvidence = TrustedScalarRead };
        });
        if (cancelAfterFirst)
        {
            // Cancel during construction of the second command, after the first
            // model has fully finalized but before a second provider dispatch.
            factory.ConfigureCommand = command =>
            {
                if (factory.Commands.Count != 1) return;
                var create = command.Creating!;
                command.Creating = () => { cancellation.Cancel(); return create(); };
            };
        }
        var work = transaction.InsertAsyncCore(original, cancellation.Token);
        try
        {
            await accesses[0].Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(factory.Inputs.Count).IsEqualTo(2);
            original.Clear();
            _ = Capture<InvalidOperationException>(() => second["Value"] = "changed" );
            await Assert.That(factory.Inputs[1].ToSql().Parameters.Select(x => x.Value).Contains("second")).IsTrue();
            accesses[0].Dispatch.Release();
            if (cancelAfterFirst)
            {
                await Assert.That(await AsyncEnumerationFailureOf(() => work)).IsTypeOf<OperationCanceledException>();
                await Assert.That(transaction.IsPoisoned).IsTrue();
                await Assert.That(transaction.Changes.Count).IsEqualTo(1);
                await Assert.That(accesses[1].Calls.Contains("dispatch:NonQuery")).IsFalse();
                await Assert.That(first.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
                await Assert.That(second.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.NoneForNew);
                _ = Capture<Exception>(transaction.Commit);
            }
            else
            {
                var result = await work;
                await Assert.That(result.Select(x => x.Id).ToArray()).IsEquivalentTo(new[] { 1, 2 });
                await Assert.That(transaction.Changes.Count).IsEqualTo(2);
            }
        }
        finally
        {
            accesses[0].Dispatch.Release();
        }
        second["Value"] = "released";

    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task AsyncMutation_UnchangedUpdateUsesCurrentLookupWithoutWriting(bool warm, bool cancel)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old baseline");
        if (warm) fixture.PrimeCommittedRow(1, "current");
        var factory = EnableAsyncMutations(fixture, null, [1, "current"]);
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();
        if (cancel) await Assert.That(await AsyncEnumerationFailureOf(() => transaction.UpdateAsyncCore(mutable, cancellation.Token))).IsTypeOf<OperationCanceledException>();
        else await Assert.That((await transaction.UpdateAsyncCore(mutable)).Value).IsEqualTo("current");
        await Assert.That(factory.Inputs).IsEmpty();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(mutable["Value"]).IsEqualTo("old baseline");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        mutable["Value"] = "released";
    }

    [Test]
    [Arguments("before")]
    [Arguments("after")]
    [Arguments("throws")]
    [Arguments("success")]
    public async Task AsyncMutation_LocalEditsHaveDefinedCancellationAndFailureOrder(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "original");
        using var cancellation = new CancellationTokenSource();
        var factory = EnableAsyncMutations(fixture, null, [1, "stored"]);
        var expected = new Exception("local editing");
        var calls = 0;
        if (mode == "before") cancellation.Cancel();
        Task<TransactionMutationGuardRow> Execute() => transaction.MutateWithEditsAsyncCore<TransactionMutationGuardRow, Mutable<TransactionMutationGuardRow>>(
            mutable, value =>
            {
                calls++;
                value["Value"] = "edited";
                if (mode == "after") cancellation.Cancel();
                if (mode == "throws") throw expected;
            }, TransactionChangeType.Update, cancellation.Token);
        if (mode == "success") await Execute();
        else
        {
            var error = await AsyncEnumerationFailureOf(Execute);
            if (mode == "throws") await Assert.That(error).IsSameReferenceAs(expected);
            else await Assert.That(error).IsTypeOf<OperationCanceledException>();
            await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(0);
            await Assert.That(mutable["Value"]).IsEqualTo(mode == "before" ? "original" : "edited");
        }
        await Assert.That(calls).IsEqualTo(mode == "before" ? 0 : 1);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        mutable["Value"] = "released";
    }

    [Test]
    public async Task AsyncMutation_EscapedArrayCannotChangeCapturedSqlAndInvalidatesAfterWrite()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingBinaryMutable([1], [2]);
        var escaped = new byte[] { 3 };
        mutable["Payload"] = escaped;
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1, FailureEvidence = TrustedScalarRead };
        var factory = EnableAsyncMutations(fixture, access);
        var work = transaction.UpdateAsyncCore(mutable);
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            escaped[0] = 99;
            var values = factory.Inputs.Single().ToSql().Parameters.Select(x => x.Value).OfType<byte[]>().ToArray();
            await Assert.That(values.Any(x => x.SequenceEqual(new byte[] { 3 }))).IsTrue();
            access.Dispatch.Release();
            await Assert.That(await AsyncEnumerationFailureOf(() => work)).IsTypeOf<InvalidOperationException>();
            await Assert.That(transaction.IsPoisoned).IsTrue();
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        }
        finally { access.Dispatch.Release(); }
    }

    [Test]
    public async Task AsyncMutation_ConfirmedDeleteDoesNotRetroactivelyCancelLocalFinalization()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var mutable = fixture.CreateExistingMutable(1, "old");
        var factory = EnableAsyncMutations(fixture);
        factory.ConfigureCommand = command => command.Resource.Disposing = cancellation.Cancel;
        await transaction.DeleteAsyncCore(mutable, cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(mutable.IsDeleted()).IsTrue();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        mutable["Value"] = "reservation released";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMutation_OrderedCleanupRetainsPrimaryAndForbidsRollback(bool repeatedException)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        var expected = new Exception("statement");
        var cleanupFailure = repeatedException ? expected : new Exception("command cleanup");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        access.Dispatch.Fail(expected);
        var factory = EnableAsyncMutations(fixture, access);
        factory.ConfigureCommand = command =>
        {
            command.Resource.Cleanup = new(paused: true);
            command.Resource.Cleanup.Fail(cleanupFailure);
        };
        var error = await AsyncEnumerationFailureOf(() => transaction.DeleteAsyncCore(mutable));
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(repeatedException ? 0 : 1);
        if (!repeatedException) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanupFailure);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(ExecutionFailureContexts.Get(error)).IsSameReferenceAs(context);
        using (transaction.ExecutionGate.Enter("failure fully published")) { }
        _ = Capture<InvalidOperationException>(transaction.Rollback);
        mutable["Value"] = "released";
    }

    [Test]
    public async Task AsyncMutation_HelperDrainsUnfinishedMutationAndItsFailureWithoutCommit()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var mutable = fixture.CreateExistingMutable(1, "old");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var factory = EnableAsyncMutations(fixture, access);
        var expected = new Exception("active mutation");
        Task? pending = null;
        var helper = transaction.RunCallbackAsyncCore(token =>
        {
            pending = transaction.DeleteAsyncCore(mutable, token);
            return Task.FromResult(19);
        }, new());
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(helper.IsCompleted).IsFalse();
            await Assert.That(fixture.Scenario.AsyncCompletion.Calls).IsEmpty();
            _ = Capture<InvalidOperationException>(mutable.Reset);
            access.Dispatch.Fail(expected);
            await Assert.That(await AsyncEnumerationFailureOf(() => pending!)).IsSameReferenceAs(expected);
            var failure = await AsyncEnumerationFailureOf(() => helper);
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(ExecutionFailureContexts.Get(failure)!.SecondaryFailures.Any(x => ReferenceEquals(x.Exception, expected))).IsTrue();
            await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
            await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        }
        finally
        {
            access.Dispatch.Release();
            if (pending is not null) { try { await pending; } catch { } }
            try { await helper; } catch { }
            await transaction.DisposeAsyncCore();
        }
        mutable["Value"] = "released";
    }

    [Test]
    public async Task AsyncMutation_InitializationFailureHasNoStatementButRemainsDisposeOnly()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("open");
        var resource = new ControlledTransactionResource { Open = new(paused: true) };
        resource.Open.Fail(expected);
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = EnableAsyncMutations(fixture);
        factory.Initialization = lazy;
        var mutable = fixture.CreateExistingMutable(1, "old");
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DeleteAsyncCore(mutable))).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        mutable["Value"] = "released";
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMutation_AssessmentFailureCannotReplaceTheStatementFailure(bool nullEvidence)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        var expected = new Exception("statement");
        var assessment = new Exception("assessment");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true));
        access.Dispatch.Fail(expected);
        if (nullEvidence) access.FailureEvidence = null!;
        else access.EvidenceFailure = assessment;
        EnableAsyncMutations(fixture, access);
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DeleteAsyncCore(mutable))).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        if (!nullEvidence) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(assessment);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        mutable["Value"] = "released";
    }
}
