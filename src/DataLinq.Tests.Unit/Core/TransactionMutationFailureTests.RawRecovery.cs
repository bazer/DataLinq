using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("non-query", false, false)]
    [Arguments("non-query", false, true)]
    [Arguments("non-query", true, false)]
    [Arguments("non-query", true, true)]
    [Arguments("scalar", false, false)]
    [Arguments("scalar", false, true)]
    [Arguments("scalar", true, false)]
    [Arguments("scalar", true, true)]
    [Arguments("typed", false, false)]
    [Arguments("typed", false, true)]
    [Arguments("typed", true, false)]
    [Arguments("typed", true, true)]
    [Arguments("reader", false, false)]
    [Arguments("reader", false, true)]
    [Arguments("reader", true, false)]
    [Arguments("reader", true, true)]
    [Arguments("rows", false, false)]
    [Arguments("rows", false, true)]
    [Arguments("rows", true, false)]
    [Arguments("rows", true, true)]
    public async Task RawRecovery_InitializationEvidenceSurvivesEnteredProviderCalls(string kind, bool asynchronous, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("provider reports failed initialization");
        // The adapter has been entered, but its classifier reports a stronger terminal
        // restriction. An optimistic rollback flag must not weaken Initialization.
        var evidence = new ReadFailureEvidence(ExecutionFailureCause.ProviderError,
            ExecutionEffects.Initialization, TransactionIntegrity.Confirmed, RollbackAvailable: true);
        var native = new ControlledAsyncDatabaseAccess(JournalFault(expected)) { FailureEvidence = evidence };
        var eager = new ControlledEagerCommandFactory { Access = native };
        var readers = RawFactory(() => native);
        fixture.Scenario.AsyncCommands = eager;
        fixture.Scenario.AsyncSqlReaders = readers;
        var sync = EnableSyncRaw(fixture);
        sync.ExecutionFailure = expected;
        sync.Evidence = evidence;
        using var command = new ControlledCommand { CommandText = "caller SQL", CommandTimeout = 23 };

        var failure = await AsyncEnumerationFailureOf(() => RunStandaloneRaw(
            transaction.DatabaseAccess, kind, asynchronous, borrowed ? command : null));

        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(asynchronous ? native.Calls.Count(call => call.StartsWith("dispatch:", StringComparison.Ordinal))
            : sync.Executions.Count).IsEqualTo(1);
        var disposals = asynchronous ? eager.Commands.Sum(item => item.Resource.AsyncDisposals)
            + readers.Commands.Sum(item => item.Resource.AsyncDisposals) : sync.CommandDisposals;
        await Assert.That(disposals).IsEqualTo(borrowed ? 0 : 1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.CommandText).IsEqualTo("caller SQL");
        await Assert.That(command.CommandTimeout).IsEqualTo(23);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(transaction.Commit);
        _ = Capture<InvalidOperationException>(transaction.Rollback);
        using (transaction.ExecutionGate.Enter("failure published before release")) { }
    }

    [Test]
    [Arguments("non-query", false)]
    [Arguments("non-query", true)]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    public async Task RawRecovery_HelperDoesNotRollbackAfterProviderInitializationFailure(string kind, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var completion = fixture.Scenario.AsyncCompletion = new();
        var expected = new Exception("initialization reported inside provider call");
        var access = new ControlledAsyncDatabaseAccess(JournalFault(expected))
        {
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Initialization,
                TransactionIntegrity.Confirmed, RollbackAvailable: true)
        };
        var factory = new ControlledEagerCommandFactory { Access = access };
        fixture.Scenario.AsyncCommands = factory;
        using var command = new ControlledCommand();
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(async _ =>
            {
                await RunStandaloneRaw(transaction.DatabaseAccess, kind, true, borrowed ? command : null);
                return 7;
            }, new()));
            await Assert.That(failure).IsSameReferenceAs(expected);
            await Assert.That(completion.Calls.Count(call => call == "rollback")).IsEqualTo(0);
            await Assert.That(completion.Calls.SequenceEqual(["dispose-transaction", "dispose-connection"])).IsTrue();
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
            await Assert.That(context.SecondaryFailures).IsEmpty();
            await Assert.That(transaction.IsDisposed).IsTrue();
            await Assert.That(access.Calls.Count(call => call.StartsWith("dispatch:", StringComparison.Ordinal))).IsEqualTo(1);
            await Assert.That(factory.Commands.Sum(item => item.Resource.AsyncDisposals)).IsEqualTo(borrowed ? 0 : 1);
            await Assert.That(command.DisposeCalls).IsEqualTo(0);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }
}
