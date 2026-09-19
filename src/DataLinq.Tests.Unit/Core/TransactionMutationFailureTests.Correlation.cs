using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("raw", false)]
    [Arguments("raw", true)]
    [Arguments("commit", false)]
    [Arguments("commit", true)]
    [Arguments("rollback", false)]
    [Arguments("rollback", true)]
    [Arguments("dispose", false)]
    [Arguments("dispose", true)]
    public async Task Correlation_ManagedRawOverlapReportsRejectedCallWithoutPoisoningActiveWork(string rejected, bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 7 };
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Access = access };
        var pending = transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("owned command");
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        ExecutionFailureContext snapshot;
        try
        {
            var failure = await AsyncEnumerationFailureOf(Reject);
            snapshot = ExecutionFailureContexts.Get(failure)!;
            var expected = rejected switch
            {
                "raw" => ExecutionOperationKind.RawCommand,
                "commit" => ExecutionOperationKind.Commit,
                "rollback" => ExecutionOperationKind.Rollback,
                _ => ExecutionOperationKind.Dispose
            };
            await Assert.That(snapshot.Operation).IsEqualTo(expected);
            await Assert.That(snapshot.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.RawCommand);
            await Assert.That(snapshot.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
            await Assert.That(snapshot.ProviderInstanceId).IsEqualTo(transaction.Provider.TelemetryInstanceId);
            await Assert.That(snapshot.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);
            await Assert.That(transaction.AsyncFailureContext).IsNull();
            await Assert.That(transaction.IsPoisoned).IsFalse();
            await Assert.That(transaction.IsDisposed).IsFalse();
            await Assert.That(access.Calls.Count(x => x == "dispatch:NonQuery")).IsEqualTo(1);
        }
        finally { access.Dispatch.Release(); await pending; }
        await Assert.That(await pending).IsEqualTo(7);
        transaction.Commit();
        await Assert.That(snapshot.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.RawCommand);
        await Assert.That(snapshot.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);

        Task Reject()
        {
            if (asynchronous) return rejected switch
            {
                "raw" => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("rejected"),
                "commit" => transaction.CommitAsyncCore(),
                "rollback" => transaction.RollbackAsyncCore(),
                _ => transaction.DisposeAsyncCore().AsTask()
            };
            switch (rejected)
            {
                case "raw": transaction.DatabaseAccess.ExecuteNonQuerySyncCore("rejected"); break;
                case "commit": transaction.Commit(); break;
                case "rollback": transaction.Rollback(); break;
                default: transaction.Dispose(); break;
            }
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task Correlation_ManagedRawFailureAndCleanupKeepProviderAndTransactionIdentity()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("raw command failed");
        var cleanup = new Exception("command cleanup failed");
        var access = new ControlledAsyncDatabaseAccess(JournalFault(primary));
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory
        {
            Access = access,
            ConfigureCommand = command => command.Resource.Cleanup = JournalFault(cleanup)
        };
        var failure = await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("owned command"));
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(transaction.Provider.TelemetryInstanceId);
        await Assert.That(context.ActiveOperation).IsNull();
        await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
    }

    [Test]
    [Arguments("non-query")]
    [Arguments("scalar")]
    [Arguments("reader")]
    public async Task Correlation_SynchronousRawFailuresUseTheSameIdentityWithoutAsyncDispatch(string family)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var expected = factory.ExecutionFailure = new Exception("native-shaped synchronous dispatch failed");
        var failure = Capture<Exception>(() =>
        {
            if (family == "non-query") transaction.DatabaseAccess.ExecuteNonQuerySyncCore("test");
            else if (family == "scalar") transaction.DatabaseAccess.ExecuteScalarSyncCore("test");
            else transaction.DatabaseAccess.ExecuteReaderSyncCore("test");
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(transaction.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(factory.Executions.Count).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correlation_ManagedCompletionFailureRetainsItsOperationAcrossDisposal(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(872, "pending"));
        var expected = new Exception("completion failed");
        if (rollback) provider.Rollback = JournalFault(expected);
        else provider.Commit = JournalFault(expected);
        var failure = await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore());
        await Assert.That(failure).IsSameReferenceAs(expected);
        var before = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(before.Operation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(before.ProviderInstanceId).IsEqualTo(transaction.Provider.TelemetryInstanceId);
        await Assert.That(before.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        // Avoid repeating the simulated failure during the independent disposal path.
        provider.Rollback = new();
        await transaction.DisposeAsyncCore();
        await Assert.That(transaction.AsyncFailureContext!.Operation).IsEqualTo(before.Operation);
        await Assert.That(transaction.AsyncFailureContext!.ProviderInstanceId).IsEqualTo(before.ProviderInstanceId);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(before.Recovery).IsNotEqualTo(ExecutionRecoveryActions.None);
    }
}
