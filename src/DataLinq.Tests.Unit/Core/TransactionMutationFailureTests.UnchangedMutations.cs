using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, "missing")]
    [Arguments(true, "missing")]
    [Arguments(false, "read-cancellation")]
    [Arguments(true, "read-cancellation")]
    [Arguments(false, "reader-cleanup")]
    [Arguments(true, "reader-cleanup")]
    [Arguments(false, "command-cleanup")]
    [Arguments(true, "command-cleanup")]
    public async Task UnchangedMutation_LookupFailureKeepsWriteHistoryAndHonorsCleanup(bool save, string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var prior = fixture.CreateExistingMutable(9, "prior work");
        transaction.Delete(prior);
        var mutable = fixture.CreateExistingMutable(1, "original baseline");
        var mutations = EnableAsyncMutations(fixture);
        var expected = new Exception(phase);
        var reader = new ControlledRowDataReader(phase == "missing" ? [] : [[1, "current"]])
            { ColumnNames = ["id", "value"] };
        if (phase == "read-cancellation") reader.Advance = new(paused: true);
        if (phase == "reader-cleanup") reader.Cleanup = new(paused: true);
        var access = new ControlledAsyncDatabaseAccess { ReaderOverride = reader, FailureEvidence = TrustedScalarRead };
        var reads = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        if (phase == "command-cleanup") reads.ConfigureCommand = command => command.Resource.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = reads;

        var work = save ? transaction.SaveAsyncCore(mutable, cancellation.Token) : transaction.UpdateAsyncCore(mutable, cancellation.Token);
        Exception failure;
        try
        {
            if (phase != "missing")
            {
                var pause = phase switch
                {
                    "read-cancellation" => reader.Advance,
                    "reader-cleanup" => reader.Cleanup,
                    _ => reads.Commands.Single().Resource.Cleanup
                };
                await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(work.IsCompleted).IsFalse();
                _ = Capture<InvalidOperationException>(() => mutable["Value"] = "conflicting edit");
                _ = Capture<InvalidOperationException>(() => transaction.Query());
                if (phase == "read-cancellation") cancellation.Cancel();
                else pause.Fail(expected);
            }
        }
        finally
        {
            reader.Advance.Release();
            reader.Cleanup.Release();
            foreach (var command in reads.Commands) command.Resource.Cleanup.Release();
            failure = await AsyncEnumerationFailureOf(() => work);
        }

        if (phase == "missing") await Assert.That(failure).IsTypeOf<ModelLoadFailureException>();
        else if (phase == "read-cancellation")
        {
            await Assert.That(failure).IsTypeOf<OperationCanceledException>();
            await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        }
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        var cleanupFailed = phase.EndsWith("cleanup", StringComparison.Ordinal);
        await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanupFailed);
        await Assert.That(context.Recovery).IsEqualTo(cleanupFailed ? ExecutionRecoveryActions.Dispose
            : ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(prior.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable["Value"]).IsEqualTo("original baseline");
        await Assert.That(mutations.Commands).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(access.Calls.Count(call => call == "dispatch:Reader")).IsEqualTo(1);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(reads.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        mutable["Value"] = "released";
        if (cleanupFailed)
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Rollback);
        }
        else
        {
            _ = transaction.Query();
            transaction.Commit();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnchangedMutation_InitializationFailureRemainsTerminalWithoutInventingAWrite(bool save)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("lookup initialization");
        var resource = new ControlledTransactionResource { Open = new(paused: true) };
        resource.Open.Fail(expected);
        var lazy = BindInitialization(fixture, transaction, resource);
        var mutations = EnableAsyncMutations(fixture);
        var access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedScalarRead };
        var reads = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source)
        };
        fixture.Scenario.AsyncSqlReaders = reads;
        var mutable = fixture.CreateExistingMutable(1, "original baseline");
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => save ? transaction.SaveAsyncCore(mutable) : transaction.UpdateAsyncCore(mutable));
            await Assert.That(failure).IsSameReferenceAs(expected);
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(mutations.Commands).IsEmpty();
            await Assert.That(reads.Commands.Single().Creates).IsEqualTo(0);
            await Assert.That(access.Calls.Any(call => call.StartsWith("dispatch:", StringComparison.Ordinal))).IsFalse();
            await Assert.That(transaction.IsPoisoned).IsFalse();
            await Assert.That(transaction.Changes).IsEmpty();
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
            await Assert.That(mutable["Value"]).IsEqualTo("original baseline");
            mutable["Value"] = "released";
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Rollback);
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
    }
}
