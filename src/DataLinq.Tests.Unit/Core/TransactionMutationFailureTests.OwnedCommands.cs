using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task OwnedReader_ReportedCommandCleanupFailureNeverRestoresTransactionReuse()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledOwnedCommandFactory();
        var cleanup = new Exception("reused exception thrown by cleanup");
        ExecutionFailureContexts.Attach(cleanup, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, []));
        factory.Resource.Cleanup = new(paused: true);
        factory.Resource.Cleanup.Fail(cleanup);
        var access = new ControlledAsyncDatabaseAccess
        {
            FailureEvidence = new(Effects: ExecutionEffects.OrdinaryRead, Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true)
        };
        var sequence = new AsyncReaderEnumerable<int>(() => new OwnedCommandExecution(access, factory, transaction.TransactionID),
            reader => reader.GetInt32(0), transaction);
        await using var rows = sequence.GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(await AsyncEnumerationFailureOf(() => rows.DisposeAsync().AsTask())).IsSameReferenceAs(cleanup);
        var context = ExecutionFailureContexts.Get(cleanup)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test]
    [Arguments("exhaustion")]
    [Arguments("early")]
    [Arguments("cancellation")]
    [Arguments("materialization")]
    public async Task OwnedReader_ManagedAdmissionSpansReaderAndCommandCleanup(string exit)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Cleanup = new(paused: true);
        var access = new ControlledAsyncDatabaseAccess
        {
            Reader = new(exit == "exhaustion" ? [] : [11]),
            FailureEvidence = new(Effects: ExecutionEffects.OrdinaryRead, Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true)
        };
        if (exit == "cancellation") access.Reader.Advance = new(paused: true);
        var materialization = new Exception("materialization");
        var sequence = new AsyncReaderEnumerable<int>(() => new OwnedCommandExecution(access, factory, transaction.TransactionID),
            reader => exit == "materialization" ? throw materialization : reader.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        Task pending = rows.MoveNextAsync().AsTask();
        if (exit == "early") { await pending; pending = rows.DisposeAsync().AsTask(); }
        if (exit == "cancellation")
        {
            await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
        }
        await factory.Resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
            await Assert.That(access.Reader.IsDisposed).IsTrue();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(factory.Resource.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally { factory.Resource.Cleanup.Release(); }
        if (exit == "materialization") await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(materialization);
        else if (exit == "cancellation") await Assert.That(await AsyncEnumerationFailureOf(() => pending) is OperationCanceledException).IsTrue();
        else await pending.WaitAsync(TimeSpan.FromSeconds(10));
        _ = transaction.Query();
        await rows.DisposeAsync();
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(0);
    }

    [Test]
    public async Task OwnedReader_HelperDrainsBothResourcesAndImportsOrderedCleanupFailures()
    {
        using var fixture = new ScriptedFixture();
        var completion = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Cleanup = new(paused: true);
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Cleanup = new(paused: true);
        var sequence = new AsyncReaderEnumerable<int>(() => new OwnedCommandExecution(access, factory, transaction.TransactionID),
            reader => reader.GetInt32(0), transaction);
        await using var rows = sequence.GetAsyncEnumerator();
        var helper = transaction.RunCallbackAsyncCore(async _ =>
        {
            await rows.MoveNextAsync();
            return 9;
        }, new());
        var readerFailure = new Exception("reader cleanup");
        var commandFailure = new Exception("command cleanup");
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
            await Assert.That(completion.Calls).IsEmpty();
        }
        finally { access.Reader.Cleanup.Fail(readerFailure); }
        await factory.Resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            await Assert.That(completion.Calls).IsEmpty();
        }
        finally { factory.Resource.Cleanup.Fail(commandFailure); }
        var error = await AsyncEnumerationFailureOf(() => helper);
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.SecondaryFailures.Select(x => x.Exception).SequenceEqual(new[] { readerFailure, commandFailure })).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(completion.Calls.SequenceEqual(new[] { "dispose-transaction", "dispose-connection" })).IsTrue();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task OwnedReader_InitializationFinishesBeforeCommandCreation_AndOwnsOnlyTheCommand()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Begin = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledOwnedCommandFactory();
        var access = new ControlledAsyncDatabaseAccess();
        var sequence = new AsyncReaderEnumerable<int>(() => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy,
            new OwnedCommandExecution(access, factory, transaction.TransactionID)), reader => reader.GetInt32(0), transaction);
        await using var rows = sequence.GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await resource.Begin.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(factory.Creates).IsEqualTo(0);
            await Assert.That(access.ObservedCommand).IsNull();
        }
        finally { resource.Begin.Release(); }
        await Assert.That(await pending).IsTrue();
        await rows.DisposeAsync();
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await Assert.That(resource.Calls.Contains("async-dispose")).IsFalse();
        await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
    }

    [Test]
    public async Task OwnedReader_SuccessfulAcquisitionWithLateCancellation_StillCleansReaderAndCommand()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var factory = new ControlledOwnedCommandFactory();
        var access = new ControlledAsyncDatabaseAccess { ReaderAcquired = cancellation.Cancel };
        var sequence = new AsyncReaderEnumerable<int>(() => new OwnedCommandExecution(access, factory, transaction.TransactionID),
            reader => reader.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask()) is OperationCanceledException).IsTrue();
        await Assert.That(access.Reader.AsyncReadCalls).IsEqualTo(0);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
    }
}
