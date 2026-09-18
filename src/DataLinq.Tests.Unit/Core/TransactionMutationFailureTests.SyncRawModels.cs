using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private static IEnumerable<TransactionMutationGuardRow> SyncRawModels(Transaction transaction, bool borrowed, IDbCommand command)
        => borrowed ? transaction.GetFromCommand<TransactionMutationGuardRow>(command)
            : transaction.GetFromQuery<TransactionMutationGuardRow>("UPDATE rows RETURNING id, value");

    private sealed partial class ScriptedDatabaseTransaction
    {
        internal override ISyncRawModelReaderSource BindRawModelReaderCore(string query)
            => scenario.SyncCommands is null ? base.BindRawModelReaderCore(query) : scenario.SyncCommands.BindCommand(query);
        internal override ISyncRawModelReaderSource BindRawModelReaderCore(IDbCommand command)
            => scenario.SyncCommands is null ? base.BindRawModelReaderCore(command) : scenario.SyncCommands.BindCommand(command);

        internal IDataLinqDataReader LegacyOwnedReader(IDbCommand command) => ExecuteOwnedReader(command);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncRawModels_ColdRepeatableAndBorrowedCommandPreservesIdentity(bool explicitBinding, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = explicitBinding ? EnableSyncRaw(fixture) : null;
        var readers = new List<OwnedReadProbe>();
        IDataLinqDataReader Create()
        {
            var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, readers.Count + 1, "raw"));
            readers.Add(reader);
            return reader;
        }
        if (factory is not null) factory.Reader = Create;
        else fixture.Scenario.ReaderFactory = Create;
        object? dispatched = null;
        fixture.Scenario.SyncPublicDispatch = value => dispatched = value;
        using var command = new ControlledCommand { CommandText = "caller command", CommandTimeout = 37 };
        var sequence = SyncRawModels(transaction, borrowed, command);
        using var rows = sequence.GetEnumerator();
        await Assert.That(readers).IsEmpty();
        if (factory is not null) await Assert.That(factory.Bindings).IsEqualTo(0);
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(1);
        await Assert.That(rows.Current.Value).IsEqualTo("raw");
        var first = rows.Current;
        _ = Capture<InvalidOperationException>(() => first.GetReadSource());
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        rows.Dispose();
        await Assert.That(first.GetReadSource()).IsSameReferenceAs(transaction);
        var second = sequence.Single();
        await Assert.That(second.Id).IsEqualTo(2);
        await Assert.That(readers.Select(x => x.Disposals).ToArray()).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.CommandText).IsEqualTo("caller command");
        await Assert.That(command.CommandTimeout).IsEqualTo(37);
        if (factory is not null)
        {
            await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 2);
            if (borrowed) await Assert.That(factory.Executions[0].Command).IsSameReferenceAs(command);
        }
        else if (borrowed) await Assert.That(dispatched).IsSameReferenceAs(command);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false, false, "acquire")]
    [Arguments(false, true, "advance")]
    [Arguments(false, false, "materialize")]
    [Arguments(false, true, "early-dispose")]
    [Arguments(false, false, "exhaust")]
    [Arguments(true, false, "acquire")]
    [Arguments(true, true, "advance")]
    [Arguments(true, false, "materialize")]
    [Arguments(true, true, "early-dispose")]
    [Arguments(true, false, "exhaust")]
    public async Task SyncRawModels_FailuresRestrictTransactionBeforeReleasingOwnership(bool explicitBinding, bool borrowed, string stage)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "pending"));
        var boundModel = fixture.CreateImmutable(2, "existing", transaction);
        var factory = explicitBinding ? EnableSyncRaw(fixture) : null;
        var expected = new Exception(stage);
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 3, "raw"));
        IDataLinqDataReader Create() => stage == "acquire" ? throw expected : reader;
        if (factory is not null) factory.Reader = Create;
        else fixture.Scenario.ReaderFactory = Create;
        if (stage == "materialize") reader.ValueFailure = expected;
        if (stage is "early-dispose" or "exhaust") reader.DisposeFailure = expected;
        reader.OnDispose = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        };
        using var command = new ControlledCommand();
        using var rows = SyncRawModels(transaction, borrowed, command).GetEnumerator();
        Exception failure;
        if (stage is "advance" or "early-dispose" or "exhaust")
        {
            await Assert.That(rows.MoveNext()).IsTrue();
            if (stage == "advance") reader.OnRead = () => throw expected;
            failure = Capture<Exception>(() => { if (stage == "early-dispose") rows.Dispose(); else rows.MoveNext(); });
        }
        else failure = Capture<Exception>(() => rows.MoveNext());
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Stage).IsEqualTo(stage switch
        {
            "acquire" => ExecutionFailureStage.CommandExecution,
            "advance" => ExecutionFailureStage.RowLoading,
            "materialize" => ExecutionFailureStage.Materialization,
            _ => ExecutionFailureStage.Cleanup
        });
        var cleanup = stage is "early-dispose" or "exhaust";
        await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanup);
        await Assert.That(context.Recovery).IsEqualTo(explicitBinding && !cleanup
            ? ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(transaction.Commit);
        _ = Capture<InvalidOperationException>(() => DataSourceAccess.EnsureReadAllowed((IDataSourceAccess)boundModel.GetReadSource(), "relation"));
        using (transaction.ExecutionGate.Enter("failure published, slot released")) { }
        if (explicitBinding && !cleanup) transaction.Rollback();
        else _ = Capture<InvalidOperationException>(transaction.Rollback);
        await Assert.That(reader.Disposals).IsEqualTo(stage == "acquire" ? 0 : 1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        if (factory is not null) await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 1);
    }

    [Test]
    [Arguments("create")]
    [Arguments("validate-created")]
    [Arguments("validate-borrowed")]
    [Arguments("null-query")]
    [Arguments("null-command")]
    public async Task SyncRawModels_PreDispatchFailurePreservesPendingWork(string stage)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "pending"));
        var factory = EnableSyncRaw(fixture);
        var expected = new Exception(stage);
        if (stage == "create") factory.CreateFailure = expected;
        if (stage.StartsWith("validate")) factory.CommandValidationFailure = expected;
        using var command = new ControlledCommand();
        var sequence = stage switch
        {
            "null-query" => transaction.GetFromQuery<TransactionMutationGuardRow>(null!),
            "null-command" => transaction.GetFromCommand<TransactionMutationGuardRow>(null!),
            _ => SyncRawModels(transaction, stage == "validate-borrowed", command)
        };
        using var rows = sequence.GetEnumerator();
        var failure = Capture<Exception>(() => rows.MoveNext());
        if (stage.StartsWith("null")) await Assert.That(failure).IsTypeOf<ArgumentNullException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Executions).IsEmpty();
        await Assert.That(factory.CommandDisposals).IsEqualTo(stage == "validate-created" ? 1 : 0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        transaction.Commit();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncRawModels_OrderedCleanupPreservesOriginalFailureAndRepeatedIdentity(bool legacyOwned, bool sameException)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("materialization");
        var cleanup = sameException ? primary : new Exception("reader cleanup");
        var commandCleanup = sameException ? primary : new Exception("command cleanup");
        var order = new List<string>();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "raw"))
        { ValueFailure = primary, DisposeFailure = cleanup, OnDispose = () => order.Add("reader") };
        var factory = legacyOwned ? null : EnableSyncRaw(fixture);
        var owned = new ScriptedDbCommand(() => { order.Add("command"); throw commandCleanup; });
        if (factory is null) fixture.Scenario.ReaderFactory = () => OwnedCommandDataReader.Create(reader, owned);
        else
        {
            factory.Reader = () => reader;
            factory.CommandDisposing = () => { order.Add("command"); throw commandCleanup; };
            factory.EvidenceFailure = new Exception("classification");
        }
        using var rows = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").GetEnumerator();
        await Assert.That(Capture<Exception>(() => rows.MoveNext())).IsSameReferenceAs(primary);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(order.ToArray()).IsEquivalentTo(new[] { "reader", "command" });
        var secondary = context.SecondaryFailures.Select(x => x.Exception).ToArray();
        await Assert.That(secondary.Length).IsEqualTo((sameException ? 0 : 2) + (legacyOwned ? 0 : 1));
        if (!sameException)
        {
            await Assert.That(secondary[0]).IsSameReferenceAs(cleanup);
            await Assert.That(secondary[1]).IsSameReferenceAs(commandCleanup);
        }
        if (factory is not null) await Assert.That(secondary[^1]).IsSameReferenceAs(factory.EvidenceFailure);
        rows.Dispose();
        await Assert.That(reader.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task SyncRawModels_LegacyAcquisitionPreservesAggregateAndCleanupEvidence()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var primary = new Exception("acquire");
        var cleanup = new Exception("command cleanup");
        var disposals = 0;
        var command = new ScriptedDbCommand(() => { disposals++; throw cleanup; });
        fixture.Scenario.SyncPublicDispatch = value =>
        {
            if (value is string) access.LegacyOwnedReader(command);
            else throw primary;
        };
        using var rows = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").GetEnumerator();
        var error = Capture<AggregateException>(() => rows.MoveNext());
        await Assert.That(error.InnerExceptions[0]).IsSameReferenceAs(primary);
        await Assert.That(error.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await Assert.That(transaction.AsyncFailureContext!.HasCleanupFailure).IsTrue();
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(ExecutionFailureContexts.Get(error)).IsSameReferenceAs(transaction.AsyncFailureContext);
        await Assert.That(disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawModels_ConstructorHoldsOwnershipAndNeverPublishesRawRows(bool fail)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        using var transaction = provider.StartTransaction();
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var cache = provider.GetTableCache(table);
        var reader = new OwnedReadProbe(new ScriptedRowData(table, 1, "raw"));
        scenario.ReaderFactory = () => reader;
        var expected = new Exception("constructor");
        var calls = 0;
        AsyncCanonicalCachedRow.Creating.Value = () =>
        {
            calls++;
            _ = Capture<InvalidOperationException>(() => DataSourceAccess.EnsureReadAllowed(transaction, "constructor reentry"));
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            if (fail) throw expected;
        };
        try
        {
            using var rows = transaction.GetFromQuery<AsyncCanonicalCachedRow>("SELECT rows").GetEnumerator();
            if (fail) await Assert.That(Capture<Exception>(() => rows.MoveNext())).IsSameReferenceAs(expected);
            else await Assert.That(rows.MoveNext()).IsTrue();
            rows.Dispose();
            await Assert.That(calls).IsEqualTo(1);
            await Assert.That(cache.RowCount).IsEqualTo(0);
            await Assert.That(cache.TransactionRowsCount).IsEqualTo(0);
            await Assert.That(transaction.Changes).IsEmpty();
            await Assert.That(reader.Disposals).IsEqualTo(1);
            if (fail) await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
            else DataSourceAccess.EnsureReadAllowed(transaction, "next read");
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawModels_HelperRejectsCaughtOrEscapedReaderFailureWithoutCommit(bool escaped)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var factory = EnableSyncRaw(fixture);
        var primary = new Exception("materialization");
        var cleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "raw"))
        { ValueFailure = escaped ? null : primary, DisposeFailure = cleanup };
        factory.Reader = () => reader;
        factory.CommandDisposing = () => throw commandCleanup;
        IEnumerator<TransactionMutationGuardRow>? rows = null;
        Exception? caught = null;
        var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(token =>
        {
            rows = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").GetEnumerator();
            if (escaped) rows.MoveNext();
            else caught = Capture<Exception>(() => rows.MoveNext());
            return Task.FromResult(9);
        }, new()));
        // An exception deliberately caught before callback completion remains on
        // that exception; the helper rejects commit via the recovery restriction.
        // Unfinished reader failures are instead collected while helper draining.
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        if (!escaped) await Assert.That(caught).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(escaped ? failure : caught!)!;
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Any(x => ReferenceEquals(x.Exception, cleanup))).IsTrue();
        await Assert.That(context.SecondaryFailures.Any(x => ReferenceEquals(x.Exception, commandCleanup))).IsTrue();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("rollback")).IsFalse();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
        rows!.Dispose();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task SyncRawModels_FailedInitializationRemainsTerminalBeforeDispatch()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var expected = new Exception("initialize");
        var resource = new ControlledTransactionResource { SyncInitializationFailure = expected };
        var lazy = BindInitialization(fixture, transaction, resource);
        factory.Initialization = lazy;
        using var rows = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").GetEnumerator();
        await Assert.That(Capture<Exception>(() => rows.MoveNext())).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Executions).IsEmpty();
        await Assert.That(factory.CommandDisposals).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.GetFromQuery<TransactionMutationGuardRow>("retry").ToArray());
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task SyncRawModels_ReusedInvocationCannotDisposeTheWinningReader()
    {
        using var leftFixture = new ScriptedFixture();
        using var rightFixture = new ScriptedFixture();
        using var left = leftFixture.Database.Transaction();
        using var right = rightFixture.Database.Transaction();
        var factory = new SyncRawTestFactory();
        var reader = new OwnedReadProbe(new ScriptedRowData(leftFixture.RowTable, 1, "raw"));
        factory.Reader = () => reader;
        ISyncRawModelReaderSource shared = factory.BindCommand("SELECT rows");
        var leftRows = SyncRawModelEnumerable.Create(left, _ => new SyncRawModelPlan<int>(shared, r => r.GetInt32(0)));
        var rightRows = SyncRawModelEnumerable.Create(right, _ => new SyncRawModelPlan<int>(shared, r => r.GetInt32(0)));
        using var winner = leftRows.GetEnumerator();
        await Assert.That(winner.MoveNext()).IsTrue();
        _ = Capture<InvalidOperationException>(() => rightRows.ToArray());
        await Assert.That(reader.Disposals).IsEqualTo(0);
        await Assert.That(factory.CommandDisposals).IsEqualTo(0);
        _ = right.Query();
        _ = Capture<InvalidOperationException>(() => left.Query());
        winner.Dispose();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
    }
}
