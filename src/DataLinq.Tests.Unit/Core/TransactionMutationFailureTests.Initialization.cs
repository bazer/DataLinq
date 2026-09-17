using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("open")]
    [Arguments("configure")]
    [Arguments("begin")]
    public async Task InitializedReader_SharesOwnershipThroughPublicationCommandAndCompletion(string phase)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var pause = PauseInitialization(resource, phase);
        var lazy = BindInitialization(fixture, transaction, resource);
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        await using var rows = InitializedRows(lazy, access, command, transaction).GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Initializing);
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(access.ObservedCommand).IsNull();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsTypeOf<InvalidOperationException>();
        }
        finally { pause.Release(); }
        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsTrue();
        await Assert.That(rows.Current).IsEqualTo(11);
        await Assert.That(lazy.PublishedResource).IsSameReferenceAs(resource);
        await Assert.That(access.ObservedCommand).IsSameReferenceAs(command);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await rows.DisposeAsync();
        await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
        await Assert.That(resource.Calls.SequenceEqual(new[] { "async-open", "async-configure", "async-begin", "async-dispose" })).IsTrue();
        await Assert.That(fixture.Scenario.AsyncCompletion!.Calls.SequenceEqual(new[] { "commit", "dispose-transaction", "dispose-connection" })).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("open", false)]
    [Arguments("configure", false)]
    [Arguments("begin", false)]
    [Arguments("open", true)]
    [Arguments("configure", true)]
    [Arguments("begin", true)]
    public async Task InitializedReader_InterruptedFirstUseIsTerminal_AndCleanupKeepsAdmission(string phase, bool cancel)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Cleanup = new(paused: true) };
        var pause = PauseInitialization(resource, phase);
        var lazy = BindInitialization(fixture, transaction, resource);
        var access = new ControlledAsyncDatabaseAccess
        {
            // An optimistic command classifier must not override failed initialization.
            FailureEvidence = new(Effects: ExecutionEffects.OrdinaryRead, Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true)
        };
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        await using var rows = InitializedRows(lazy, access, command, transaction, cancellation.Token).GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("initialization failed");
        if (cancel) cancellation.Cancel();
        else pause.Fail(expected);
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(resource.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
            await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DisposeAsyncCore().AsTask())).IsTypeOf<InvalidOperationException>();
        }
        finally { resource.Cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        if (cancel) await Assert.That(failure is OperationCanceledException).IsTrue();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.Cause).IsEqualTo(cancel ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(access.Calls.Contains("assess-failure")).IsFalse();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsTypeOf<InvalidOperationException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.RollbackAsyncCore())).IsTypeOf<InvalidOperationException>();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await using var later = InitializedRows(lazy, access, command, transaction, new(true)).GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => later.MoveNextAsync().AsTask())).IsTypeOf<InvalidOperationException>();
        await transaction.DisposeAsyncCore();
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(1);
        await Assert.That(fixture.Scenario.AsyncCompletion!.Calls.SequenceEqual(new[] { "dispose-transaction", "dispose-connection" })).IsTrue();
    }

    [Test]
    public async Task InitializedReader_PreCancellationAndInvalidCapability_DoNotInitializeOrPoison()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var lazy = BindInitialization(fixture, transaction, resource);
        var access = new ControlledAsyncDatabaseAccess { ValidationFailure = new NotSupportedException("unsupported reader") };
        using var command = new ControlledCommand();
        await using var invalid = InitializedRows(lazy, access, command, transaction, new(true)).GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => invalid.MoveNextAsync().AsTask())).IsSameReferenceAs(access.ValidationFailure);
        access.ValidationFailure = null;
        await using var canceled = InitializedRows(lazy, access, command, transaction, new(true)).GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => canceled.MoveNextAsync().AsTask()) is OperationCanceledException).IsTrue();
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
        await Assert.That(resource.Calls).IsEmpty();
    }

    [Test]
    public async Task InitializedReader_CancellationAfterSuccessfulInitialization_PreservesReadyStateAndLaterExecution()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Initialized = cancellation.Cancel };
        var lazy = BindInitialization(fixture, transaction, resource);
        var access = new ControlledAsyncDatabaseAccess
        {
            FailureEvidence = new(RollbackAvailable: true)
        };
        using var command = new ControlledCommand();
        await using var canceled = InitializedRows(lazy, access, command, transaction, cancellation.Token).GetAsyncEnumerator();
        var failure = await AsyncEnumerationFailureOf(() => canceled.MoveNextAsync().AsTask());
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await Assert.That(lazy.Failure).IsNull();
        await Assert.That(access.ObservedCommand).IsNull();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        _ = transaction.Query();
        await using var later = InitializedRows(lazy, access, command, transaction).GetAsyncEnumerator();
        await Assert.That(await later.MoveNextAsync()).IsTrue();
        await later.DisposeAsync();
        await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
    }

    [Test]
    public async Task InitializedReader_AwaitedHelperFailure_PreservesInitializationAndCleanupDiagnostics()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Open = new(paused: true), Cleanup = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var expected = new Exception("open");
        var cleanup = new Exception("partial initialization cleanup");
        resource.Open.Fail(expected);
        resource.Cleanup.Fail(cleanup);
        using var command = new ControlledCommand();
        var error = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(async token =>
        {
            await using var rows = InitializedRows(lazy, new(), command, transaction, token).GetAsyncEnumerator();
            return await rows.MoveNextAsync();
        }, new()));
        await Assert.That(error).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        // The disposal retry throws the same cleanup instance; it is retained once.
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures[0].Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(2);
        await Assert.That(fixture.Scenario.AsyncCompletion!.Calls.SequenceEqual(new[] { "dispose-transaction", "dispose-connection" })).IsTrue();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task InitializedReader_UnfinishedHelper_DrainsInitializationAndRetainedResourceWithoutCommitOrRollback()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Open = new(paused: true), Cleanup = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var completion = fixture.Scenario.AsyncCompletion!;
        completion.TransactionCleanup = new(paused: true);
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        await using var rows = InitializedRows(lazy, access, command, transaction).GetAsyncEnumerator();
        Task<bool>? read = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            read = rows.MoveNextAsync().AsTask();
            return Task.FromResult(7);
        }, new());
        await resource.Open.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var initialization = new Exception("initialization");
        var cleanup = new Exception("partial cleanup");
        var disposal = new Exception("retained resource disposal");
        var connection = new Exception("connection disposal");
        resource.Open.Fail(initialization);
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            await Assert.That(completion.Calls).IsEmpty();
        }
        finally { resource.Cleanup.Fail(cleanup); }
        await completion.TransactionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var readError = await AsyncEnumerationFailureOf(() => read!);
        await Assert.That(readError).IsSameReferenceAs(initialization);
        var readContext = ExecutionFailureContexts.Get(readError)!;
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            await Assert.That(readContext.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
            await Assert.That(readContext.SecondaryFailures.Count).IsEqualTo(1);
            await Assert.That(readContext.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        }
        finally
        {
            resource.Cleanup = new(paused: true);
            resource.Cleanup.Fail(disposal);
            completion.ConnectionCleanup = new(paused: true);
            completion.ConnectionCleanup.Fail(connection);
            completion.TransactionCleanup.Release();
        }
        var helperError = await AsyncEnumerationFailureOf(() => helper);
        await Assert.That(helperError).IsTypeOf<InvalidOperationException>();
        var final = ExecutionFailureContexts.Get(helperError)!;
        await Assert.That(final.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(final.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(final.SecondaryFailures.Select(x => x.Exception).SequenceEqual(new[] { initialization, cleanup, disposal, connection })).IsTrue();
        await Assert.That(readContext.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(completion.Calls.SequenceEqual(new[] { "dispose-transaction", "dispose-connection" })).IsTrue();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task InitializedReader_CancellationAfterAdmissionBeforeInitialization_LeavesUnusedTransactionReusable()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var lazy = BindInitialization(fixture, transaction, resource);
        using var cancellation = new CancellationTokenSource();
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess();
        var sequence = new AsyncReaderEnumerable<int>(() => new CancelBeforeInitializationSource(
            new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, new BorrowedCommandReaderSource(access, command)), cancellation),
            row => row.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        var error = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
        await Assert.That(resource.Calls).IsEmpty();
    }

    [Test]
    public async Task InitializedReader_MissingManagedOwnerFailsBeforeCancellationAndNativeWork()
    {
        var gate = new TransactionOperationGate(71);
        var resource = new ControlledTransactionResource();
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        var source = new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, new BorrowedCommandReaderSource(access, command));
        var sequence = new AsyncReaderEnumerable<int>(() => source, row => row.GetInt32(0), cancellationToken: new(true));
        await using var rows = sequence.GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask())).IsTypeOf<InvalidOperationException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => source.OpenReaderAsync(new(true)))).IsTypeOf<InvalidOperationException>();
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(access.ObservedCommand).IsNull();
    }

    private sealed class CancelBeforeInitializationSource(IAsyncTransactionReaderSource source, CancellationTokenSource cancellation)
        : IAsyncTransactionReaderSource, IAsyncReadFailureEvidence
    {
        public void Validate() => source.Validate();
        public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken token) => throw new InvalidOperationException("Missing owner.");
        public Task<IAsyncDataReader> OpenReaderAsync(TransactionOperationGate.Step owner, CancellationToken token)
        {
            cancellation.Cancel();
            return source.OpenReaderAsync(owner, token);
        }
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => ((IAsyncReadFailureEvidence)source).GetReadFailureEvidence(failure);
    }

    private static LazyTransactionResource<ControlledTransactionResource> BindInitialization(
        ScriptedFixture fixture, Transaction transaction, ControlledTransactionResource resource)
    {
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(transaction.ExecutionGate, () => resource);
        fixture.Scenario.AsyncCompletion = new()
        {
            InspectInitialization = () => lazy.State,
            DisposeResource = lazy.DisposeAsync
        };
        return lazy;
    }

    private static AsyncReaderEnumerable<int> InitializedRows(LazyTransactionResource<ControlledTransactionResource> resource,
        ControlledAsyncDatabaseAccess access, ControlledCommand command, Transaction transaction, CancellationToken token = default) =>
        new(() => new InitializingTransactionReaderSource<ControlledTransactionResource>(resource, new BorrowedCommandReaderSource(access, command)),
            row => row.GetInt32(0), transaction, token);

    private static AsyncCheckpoint PauseInitialization(ControlledTransactionResource resource, string phase)
    {
        var pause = new AsyncCheckpoint(paused: true);
        switch (phase)
        {
            case "open": resource.Open = pause; break;
            case "configure": resource.Configure = pause; break;
            case "begin": resource.Begin = pause; break;
            default: throw new ArgumentOutOfRangeException(nameof(phase));
        }
        return pause;
    }
}
