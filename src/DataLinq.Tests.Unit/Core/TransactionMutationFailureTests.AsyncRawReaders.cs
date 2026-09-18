using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private static Task<IAsyncDataReader> OpenRawReader(DatabaseAccess access, bool borrowed, ControlledCommand command, CancellationToken token = default)
        => borrowed ? access.ExecuteReaderAsyncCore(command, token) : access.ExecuteReaderAsyncCore("UPDATE rows RETURNING value", token);

    private static IAsyncEnumerable<IDataLinqDataReader> RawReaderRows(DatabaseAccess access, bool borrowed, ControlledCommand command, CancellationToken token = default)
        => borrowed ? access.ReadReaderAsyncCore(command, token) : access.ReadReaderAsyncCore("UPDATE rows RETURNING value", token);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_OwnAdmissionFromAcquisitionThroughEofAndCleanup(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand { CommandText = "caller statement", CommandTimeout = 31 };
        var access = new ControlledAsyncDatabaseAccess(new(paused: true));
        access.Reader.Cleanup = new(paused: true);
        var factory = RawFactory(() => access);
        factory.ConfigureCommand = owned => owned.Resource.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = OpenRawReader(transaction.DatabaseAccess, borrowed, command);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { _ = Capture<InvalidOperationException>(() => transaction.Query()); }
        finally { access.Dispatch.Release(); }
        var reader = await pending;
        await Assert.That(reader.GetOrdinal("Value")).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => reader.GetInt32(0));
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(11);
        _ = Capture<InvalidOperationException>(() => transaction.Delete(fixture.CreateImmutable(123, "value")));
        _ = Capture<InvalidOperationException>(transaction.Commit);
        _ = Capture<InvalidOperationException>(transaction.Rollback);
        _ = Capture<InvalidOperationException>(transaction.Dispose);
        await Assert.That(await AsyncEnumerationFailureOf(() => OpenRawReader(transaction.DatabaseAccess, borrowed, command))).IsTypeOf<InvalidOperationException>();
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        await Assert.That(await reader.ReadNextRowAsync(default)).IsFalse();
        _ = Capture<InvalidOperationException>(() => reader.GetInt32(0));
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        var closing = reader.DisposeAsync().AsTask();
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(closing.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => reader.DisposeAsync());
        }
        finally { access.Reader.Cleanup.Release(); }
        if (!borrowed)
        {
            var cleanup = factory.Commands[0].Resource.Cleanup;
            await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            try { _ = Capture<InvalidOperationException>(() => transaction.Query()); }
            finally { cleanup.Release(); }
        }
        await closing;
        await reader.DisposeAsync();
        reader.Dispose();
        _ = Capture<ObjectDisposedException>(() => reader.GetInt32(0));
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.CommandText).IsEqualTo("caller statement");
        await Assert.That(command.CommandTimeout).IsEqualTo(31);
        if (borrowed) await Assert.That(access.ObservedCommand).IsSameReferenceAs(command);
        else await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(transaction.Changes).IsEmpty();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawReaders_PreCancellationAndCapabilityDoNotAcquire(bool borrowed, bool unsupported)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var lazy = BindInitialization(fixture, transaction, resource);
        var expected = new NotSupportedException("capability");
        var access = new ControlledAsyncDatabaseAccess { ValidationFailure = unsupported ? expected : null };
        var factory = RawFactory(() => access);
        factory.ConfigureCommand = command => command.ValidationFailure = unsupported ? expected : null;
        factory.WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source);
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        var failure = await AsyncEnumerationFailureOf(() => OpenRawReader(transaction.DatabaseAccess, borrowed, command, new(true)));
        await Assert.That(unsupported ? ReferenceEquals(failure, expected) : failure is OperationCanceledException).IsTrue();
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = transaction.Query();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false, "open")]
    [Arguments(false, "configure")]
    [Arguments(false, "begin")]
    [Arguments(true, "open")]
    [Arguments(true, "configure")]
    [Arguments(true, "begin")]
    public async Task AsyncRawReaders_FailedInitializationStaysPrivateUntilCleanup(bool borrowed, string phase)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Cleanup = new(paused: true) };
        var pause = PauseInitialization(resource, phase);
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = RawFactory(() => new() { FailureEvidence = TrustedScalarRead });
        factory.WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source);
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        var pending = OpenRawReader(transaction.DatabaseAccess, borrowed, command);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("initialization");
        pause.Fail(expected);
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(lazy.PublishedResource).IsNull();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { resource.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_CancellationAfterInitializationBeforeDispatchPreservesReadyState(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Initialized = cancellation.Cancel };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = RawFactory(() => new() { FailureEvidence = TrustedScalarRead });
        factory.WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source);
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        await Assert.That(await AsyncEnumerationFailureOf(() => OpenRawReader(transaction.DatabaseAccess, borrowed, command, cancellation.Token))).IsTypeOf<OperationCanceledException>();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await using (var reader = await OpenRawReader(transaction.DatabaseAccess, borrowed, command))
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawReaders_DispatchFailureCannotClaimOrdinaryReadOrNoStatement(bool borrowed, bool noStatement)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("dispatch");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
        {
            FailureEvidence = TrustedScalarRead with { Effects = noStatement ? ExecutionEffects.NoStatement : ExecutionEffects.OrdinaryRead }
        };
        var factory = RawFactory(() => access);
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        var pending = OpenRawReader(transaction.DatabaseAccess, borrowed, command);
        access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        if (!borrowed) await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        transaction.Rollback();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawReaders_FailedOrCanceledAdvanceCleansUpBeforePublishingRecovery(bool borrowed, bool cancel)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedScalarRead };
        access.Reader.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        using var command = new ControlledCommand();
        var reader = await OpenRawReader(transaction.DatabaseAccess, borrowed, command);
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        access.Reader.Advance = new(paused: true);
        var pending = reader.ReadNextRowAsync(cancellation.Token);
        await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("advance");
        if (cancel) cancellation.Cancel(); else access.Reader.Advance.Fail(expected);
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(transaction.AsyncFailureContext).IsNull();
        }
        finally { access.Reader.Cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(transaction.AsyncFailureContext);
        await reader.DisposeAsync();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.Cleanup.ObservedToken.CanBeCanceled).IsFalse();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncRawReaders_RejectConcurrentAdvanceGetterAndDisposeWithoutDisturbingActiveMove()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess();
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        await using var reader = await transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value");
        await reader.ReadNextRowAsync(default);
        access.Reader.Advance = new(paused: true);
        var pending = reader.ReadNextRowAsync(default);
        await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            _ = Capture<InvalidOperationException>(() => reader.ReadNextRowAsync(default));
            _ = Capture<InvalidOperationException>(() => reader.ReadNextRow());
            _ = Capture<InvalidOperationException>(() => reader.GetInt32(0));
            _ = Capture<InvalidOperationException>(() => reader.GetOrdinal("Value"));
            _ = Capture<InvalidOperationException>(reader.Dispose);
            _ = Capture<InvalidOperationException>(() => reader.DisposeAsync());
            await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
            await Assert.That(transaction.AsyncFailureContext).IsNull();
        }
        finally { access.Reader.Advance.Release(); }
        await Assert.That(await pending).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(22);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawReaders_SequentialSyncAndAsyncUseShareOneDisposalState(bool borrowed, bool syncDispose)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.AllowSynchronousCalls = true;
        var factory = RawFactory(() => access);
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        var reader = await OpenRawReader(transaction.DatabaseAccess, borrowed, command);
        await Assert.That(reader.ReadNextRow()).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(11);
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(22);
        if (syncDispose) reader.Dispose(); else await reader.DisposeAsync();
        reader.Dispose();
        await reader.DisposeAsync();
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(syncDispose ? 2 : 1);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(syncDispose ? 0 : 1);
        if (!borrowed)
        {
            await Assert.That(factory.Commands[0].Resource.SyncDisposals).IsEqualTo(syncDispose ? 1 : 0);
            await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(syncDispose ? 0 : 1);
        }
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_CleanupAndAssessmentFailuresKeepOriginalAndEncounterOrder(bool failedAdvance)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var execution = new Exception("read");
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var assessment = new Exception("assessment");
        var access = new ControlledAsyncDatabaseAccess { EvidenceFailure = assessment };
        access.Reader.Cleanup = new(paused: true);
        access.Reader.Cleanup.Fail(readerCleanup);
        var factory = RawFactory(() => access);
        factory.ConfigureCommand = command => command.Resource.Disposing = () => throw commandCleanup;
        fixture.Scenario.AsyncSqlReaders = factory;
        var reader = await transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value");
        Exception failure;
        if (failedAdvance)
        {
            access.Reader.Advance = new(paused: true);
            access.Reader.Advance.Fail(execution);
            failure = await AsyncEnumerationFailureOf(() => reader.ReadNextRowAsync(default));
        }
        else failure = await AsyncEnumerationFailureOf(() => reader.DisposeAsync().AsTask());
        await Assert.That(failure).IsSameReferenceAs(failedAdvance ? execution : readerCleanup);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        var secondary = context.SecondaryFailures.Select(x => x.Exception).ToArray();
        await Assert.That(secondary.Length).IsEqualTo(failedAdvance ? 3 : 2);
        if (failedAdvance) await Assert.That(secondary[0]).IsSameReferenceAs(readerCleanup);
        await Assert.That(secondary[^2]).IsSameReferenceAs(commandCleanup);
        await Assert.That(secondary[^1]).IsSameReferenceAs(assessment);
        await reader.DisposeAsync();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncRawReaders_LateAcquisitionCancellationStillTransfersTheReader()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess { ReaderAcquired = cancellation.Cancel };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        var reader = await transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value", cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        await reader.DisposeAsync();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_DeferredSequenceIsColdAndYieldsOneEphemeralReader(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = RawFactory(() => new());
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        var sequence = RawReaderRows(transaction.DatabaseAccess, borrowed, command);
        await Assert.That(factory.Accesses).IsEmpty();
        await using (var unused = sequence.GetAsyncEnumerator()) { }
        await Assert.That(factory.Accesses[0].Calls).IsEmpty();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        var replacement = RawFactory(() => new());
        fixture.Scenario.AsyncSqlReaders = replacement;
        var rows = sequence.GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var first = rows.Current;
        await Assert.That(first.GetInt32(0)).IsEqualTo(11);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current).IsSameReferenceAs(first);
        await Assert.That(first.GetInt32(0)).IsEqualTo(22);
        await rows.DisposeAsync(); // Early termination rather than exhaustion.
        await Assert.That(replacement.Accesses[0].Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawReaders_SequenceCombinesTokensAndRejectsRawReadReuse(bool borrowed, bool cancelMethod)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedScalarRead };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        using var command = new ControlledCommand();
        await using var rows = RawReaderRows(transaction.DatabaseAccess, borrowed, command, method.Token).GetAsyncEnumerator(enumeration.Token);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        if (cancelMethod) method.Cancel(); else enumeration.Cancel();
        var failure = await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); });
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawReaders_RawModelFailuresRejectProviderClaimsOfHarmlessEffects(bool borrowed, bool noStatement)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var native = new ControlledRowDataReader([1, "value"]) { ColumnNames = ["id", "value"] };
        var access = new ControlledAsyncDatabaseAccess { ReaderOverride = native,
            FailureEvidence = TrustedScalarRead with { Effects = noStatement ? ExecutionEffects.NoStatement : ExecutionEffects.OrdinaryRead } };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        using var command = new ControlledCommand();
        await using var rows = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command).GetAsyncEnumerator();
        await rows.MoveNextAsync();
        native.Advance = new(paused: true);
        var expected = new Exception("raw model read");
        native.Advance.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); })).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(native.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_HelperDrainsReturnedReaderAndStopsEscapedCalls(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        using var command = new ControlledCommand();
        IAsyncDataReader? escaped = null;
        var helper = transaction.RunCallbackAsyncCore(async _ =>
        {
            escaped = await OpenRawReader(transaction.DatabaseAccess, borrowed, command);
            await escaped.ReadNextRowAsync(default);
            return 9;
        }, new());
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => escaped!.ReadNextRowAsync(default));
            _ = Capture<InvalidOperationException>(() => escaped!.GetInt32(0));
        }
        finally { access.Reader.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await escaped!.DisposeAsync();
        escaped.Dispose();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_HelperDrainsAcquisitionOrActiveAdvanceAndReportsFailure(bool duringAcquisition)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var access = new ControlledAsyncDatabaseAccess(new(paused: duringAcquisition)) { FailureEvidence = TrustedScalarRead };
        var factory = RawFactory(() => access);
        factory.ConfigureCommand = command => command.Resource.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = factory;
        Task? unfinished = null;
        var helper = transaction.RunCallbackAsyncCore(async _ =>
        {
            var opening = transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value");
            if (duringAcquisition) unfinished = opening;
            else
            {
                var reader = await opening;
                access.Reader.Advance = new(paused: true);
                unfinished = reader.ReadNextRowAsync(default);
            }
            return 9;
        }, new());
        var pause = duringAcquisition ? access.Dispatch : access.Reader.Advance;
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("unfinished raw reader");
        pause.Fail(expected);
        var cleanup = factory.Commands[0].Resource.Cleanup;
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { await Assert.That(helper.IsCompleted).IsFalse(); }
        finally { cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => unfinished!)).IsSameReferenceAs(expected);
        var failure = await AsyncEnumerationFailureOf(() => helper);
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(ExecutionFailureContexts.Get(failure)!.SecondaryFailures.Any(x => ReferenceEquals(x.Exception, expected))).IsTrue();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncRawReaders_PrivateDispatchUsesExistingOwnerAndRejectsForeignOwner()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new());
        using var command = new ControlledCommand();
        using (var ownership = DataSourceAccess.BeginRead(transaction, "managed reader")!)
        {
            await using var reader = await transaction.DatabaseAccess.ExecuteReaderOwnedAsyncCore(command, ownership.Step, default);
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteReaderAsyncCore(command))).IsTypeOf<InvalidOperationException>();
            await Assert.That(await AsyncEnumerationFailureOf(() => other.DatabaseAccess.ExecuteReaderOwnedAsyncCore(command, ownership.Step, default))).IsTypeOf<InvalidOperationException>();
        }
        _ = transaction.Query();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncRawReaders_StandaloneReadersCanRemainActiveIndependently()
    {
        using var fixture = new ScriptedFixture();
        var factory = RawFactory(() => new(new(paused: true)));
        fixture.Scenario.AsyncSqlReaders = factory;
        var access = fixture.Provider.ReadOnlyAccess.DatabaseAccess;
        var first = access.ExecuteReaderAsyncCore("SELECT first");
        var second = access.ExecuteReaderAsyncCore("SELECT second");
        await Task.WhenAll(factory.Accesses.Select(x => x.Dispatch.Entered)).WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var native in factory.Accesses) native.Dispatch.Release();
        await using var one = await first;
        await using var two = await second;
        await Assert.That(await one.ReadNextRowAsync(default)).IsTrue();
        await Assert.That(await two.ReadNextRowAsync(default)).IsTrue();
        await one.DisposeAsync();
        await Assert.That(await two.ReadNextRowAsync(default)).IsTrue();
        await two.DisposeAsync();
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_HelperDrainsEscapedEphemeralSequence(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        using var command = new ControlledCommand();
        IAsyncEnumerator<IDataLinqDataReader>? escaped = null;
        var helper = transaction.RunCallbackAsyncCore(async _ =>
        {
            escaped = RawReaderRows(transaction.DatabaseAccess, borrowed, command).GetAsyncEnumerator();
            await escaped.MoveNextAsync();
            return 9;
        }, new());
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => { _ = escaped!.Current; });
            _ = Capture<InvalidOperationException>(() => escaped!.MoveNextAsync());
        }
        finally { access.Reader.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await escaped!.DisposeAsync();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_DispatchAndCommandCleanupFailureRetainPrimary(bool sameException)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("dispatch");
        var cleanup = sameException ? expected : new Exception("command cleanup");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var factory = RawFactory(() => access);
        factory.ConfigureCommand = command => command.Resource.Disposing = () => throw cleanup;
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value");
        access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(sameException ? 0 : 1);
        if (!sameException) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_CanceledInitializationIsDrainedBeforeReturningFailure(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Begin = new(paused: true), Cleanup = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = RawFactory(() => new() { FailureEvidence = TrustedScalarRead });
        factory.WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source);
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new ControlledCommand();
        var pending = OpenRawReader(transaction.DatabaseAccess, borrowed, command, cancellation.Token);
        await resource.Begin.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(lazy.PublishedResource).IsNull();
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        finally { resource.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<OperationCanceledException>();
        await Assert.That(transaction.AsyncFailureContext!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(transaction.AsyncFailureContext.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncRawReaders_UnsupportedActualCommandCannotUseDbCommandSynchronousDefaults()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = RawFactory(() => new());
        fixture.Scenario.AsyncSqlReaders = factory;
        using var command = new UnverifiedCommand();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteReaderAsyncCore(command, new(true)))).IsTypeOf<NotSupportedException>();
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncRawReaders_PreDispatchConstructionFailureDoesNotRestrictPriorWork()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(fixture.CreateImmutable(1, "delete"));
        var expected = new Exception("construct command");
        var factory = RawFactory(() => new() { FailureEvidence = TrustedScalarRead });
        factory.ConfigureCommand = command => command.Creating = () => throw expected;
        fixture.Scenario.AsyncSqlReaders = factory;
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value"))).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        transaction.Commit();
    }

    [Test]
    public async Task AsyncRawReaders_SequentialBorrowedOpenRecordsOnlyCurrentDispatch()
    {
        var access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedScalarRead };
        using var command = new ControlledCommand();
        var source = new BorrowedCommandReaderSource(access, command);
        await using (var reader = await source.OpenReaderAsync(default)) { }
        var failure = await AsyncEnumerationFailureOf(() => source.OpenReaderAsync(new(true)));
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(source.CommandDispatched).IsFalse();
        await Assert.That(source.GetReadFailureEvidence(failure).Effects).IsEqualTo(ExecutionEffects.NoStatement);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawReaders_SourceWithoutDispatchObservationRemainsConservative(bool initialization)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("custom source");
        var evidence = TrustedScalarRead with { Effects = initialization ? ExecutionEffects.Initialization : ExecutionEffects.OrdinaryRead };
        var factory = RawFactory(() => new());
        factory.WrapSource = _ => new ClassifiedRawSource(expected, evidence);
        fixture.Scenario.AsyncSqlReaders = factory;
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value"))).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(initialization
            ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.AsyncFailureContext.Stage).IsEqualTo(initialization
            ? ExecutionFailureStage.Initialization : ExecutionFailureStage.CommandExecution);
    }

    private sealed class ClassifiedRawSource(Exception failure, ReadFailureEvidence evidence) : IAsyncReaderSource, IAsyncReadFailureEvidence
    {
        public void Validate() { }
        public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken token) => Task.FromException<IAsyncDataReader>(failure);
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => evidence;
    }
}
