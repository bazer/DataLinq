using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments("non-query", false, "none")]
    [Arguments("non-query", false, "initialization")]
    [Arguments("non-query", false, "lost")]
    [Arguments("non-query", true, "none")]
    [Arguments("non-query", true, "initialization")]
    [Arguments("non-query", true, "lost")]
    [Arguments("scalar", false, "none")]
    [Arguments("scalar", false, "initialization")]
    [Arguments("scalar", false, "lost")]
    [Arguments("scalar", true, "none")]
    [Arguments("scalar", true, "initialization")]
    [Arguments("scalar", true, "lost")]
    [Arguments("reader", false, "none")]
    [Arguments("reader", false, "initialization")]
    [Arguments("reader", false, "lost")]
    [Arguments("reader", true, "none")]
    [Arguments("reader", true, "initialization")]
    [Arguments("reader", true, "lost")]
    [Arguments("rows", false, "none")]
    [Arguments("rows", false, "initialization")]
    [Arguments("rows", false, "lost")]
    [Arguments("rows", true, "none")]
    [Arguments("rows", true, "initialization")]
    [Arguments("rows", true, "lost")]
    public async Task NoDispatchRestrictions_RawAsyncKeepsProviderRestrictions(string kind, bool borrowed, string restriction)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "pending"));
        var expected = new Exception("telemetry start");
        var native = new ControlledAsyncDatabaseAccess
        {
            TelemetryAccess = transaction.DatabaseAccess, FailureEvidence = NoDispatchRestrictionEvidence(restriction)
        };
        var eager = new ControlledEagerCommandFactory { Access = native };
        var readers = RawFactory(() => native);
        fixture.Scenario.AsyncCommands = eager;
        fixture.Scenario.AsyncSqlReaders = readers;
        using var command = new ControlledCommand();
        Exception failure;
        using (var activities = new CommandActivityProbe(starting: _ => throw expected))
            failure = await AsyncEnumerationFailureOf(() => RunStandaloneRaw(transaction.DatabaseAccess, kind, true, borrowed ? command : null));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(context.Recovery).IsEqualTo(restriction == "none"
            ? ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose
            : ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(native.ObservedCommand).IsNull();
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(1);
        await Assert.That(native.Reader.AsyncReadCalls).IsEqualTo(0);
        await Assert.That(native.Reader.AsyncDisposeCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(eager.Commands.Sum(x => x.Resource.AsyncDisposals) + readers.Commands.Sum(x => x.Resource.AsyncDisposals)).IsEqualTo(borrowed ? 0 : 1);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        if (restriction == "none") _ = transaction.Query();
        else _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test]
    [Arguments("reader", "none")]
    [Arguments("reader", "initialization")]
    [Arguments("reader", "lost")]
    [Arguments("scalar", "none")]
    [Arguments("scalar", "initialization")]
    [Arguments("scalar", "lost")]
    public async Task NoDispatchRestrictions_PostInitializationCancellationKeepsProviderRestrictions(string kind, string restriction)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Initialized = cancellation.Cancel };
        var lazy = BindInitialization(fixture, transaction, resource);
        // Classify without a lower command adapter so it cannot mask an incorrect
        // normalization performed by the initialization wrapper itself.
        var source = new NoDispatchEvidenceSource(NoDispatchRestrictionEvidence(restriction));
        var factory = new ControlledSqlReaderFactory
        {
            WrapScalar = _ => new InitializingTransactionScalarSource<ControlledTransactionResource>(lazy, source)
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        try
        {
            var failure = await AsyncEnumerationFailureOf(async () =>
            {
                if (kind == "scalar")
                    await transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore(cancellation.Token);
                else
                {
                    var rows = new AsyncReaderEnumerable<int>(
                        () => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source),
                        row => row.GetInt32(0), transaction, cancellation.Token);
                    await using var iterator = rows.GetAsyncEnumerator();
                    _ = await iterator.MoveNextAsync();
                }
            });
            await Assert.That(failure).IsTypeOf<OperationCanceledException>();
            await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
            var context = transaction.AsyncFailureContext!;
            await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
            await Assert.That(context.Recovery).IsEqualTo(restriction == "none"
                ? ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose
                : ExecutionRecoveryActions.Dispose);
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
            await Assert.That(source.Dispatches).IsEqualTo(0);
            await Assert.That(source.Assessments).IsEqualTo(1);
            await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(0);
            if (restriction == "none") _ = transaction.Query();
            else _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(1);
    }

    private static ReadFailureEvidence NoDispatchRestrictionEvidence(string restriction) => new(
        ExecutionFailureCause.ProviderError,
        restriction == "initialization" ? ExecutionEffects.Initialization : ExecutionEffects.OrdinaryRead,
        restriction == "lost" ? TransactionIntegrity.Lost : TransactionIntegrity.Confirmed, RollbackAvailable: true);

    private sealed class NoDispatchEvidenceSource(ReadFailureEvidence evidence) : IAsyncReaderSource, IAsyncScalarSource, IAsyncReadFailureEvidence
    {
        internal int Dispatches { get; private set; }
        internal int Assessments { get; private set; }
        public void Validate() { }
        public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken token)
        {
            Dispatches++;
            throw new InvalidOperationException("Cancellation must prevent reader dispatch.");
        }
        public Task<object?> ExecuteScalarAsync(CancellationToken token)
        {
            Dispatches++;
            throw new InvalidOperationException("Cancellation must prevent scalar dispatch.");
        }
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) { Assessments++; return evidence; }
    }
}
