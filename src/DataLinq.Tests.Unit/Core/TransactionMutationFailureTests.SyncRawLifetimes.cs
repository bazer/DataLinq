using System;
using System.Data;
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
    public async Task SyncRawCommands_BorrowedValidationAndMissingCapabilityDoNotInvalidatePendingWork()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "pending"));
        _ = Capture<NotSupportedException>(() => transaction.DatabaseAccess.ExecuteScalarSyncCore("unsupported"));
        var factory = EnableSyncRaw(fixture);
        var expected = new NotSupportedException("borrowed command");
        factory.CommandValidationFailure = expected;
        using var command = new ControlledCommand();
        await Assert.That(Capture<NotSupportedException>(() => transaction.DatabaseAccess.ExecuteScalar(command))).IsSameReferenceAs(expected);
        await Assert.That(factory.Executions).IsEmpty();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        transaction.Commit();
    }

    [Test]
    [Arguments("commit")]
    [Arguments("rollback")]
    [Arguments("dispose")]
    public async Task SyncRawCommands_TerminalAndNullValidationPrecedeBinding(string completion)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        _ = Capture<ArgumentNullException>(() => transaction.DatabaseAccess.ExecuteScalar((string)null!));
        _ = Capture<ArgumentNullException>(() => transaction.DatabaseAccess.ExecuteScalar<int>((IDbCommand)null!));
        _ = Capture<ArgumentNullException>(() => transaction.DatabaseAccess.ExecuteNonQuery((IDbCommand)null!));
        _ = Capture<ArgumentNullException>(() => transaction.DatabaseAccess.ExecuteReader((string)null!));
        using var rows = transaction.DatabaseAccess.ReadReaderSyncCore("SELECT rows").GetEnumerator();
        if (completion == "commit") transaction.Commit();
        else if (completion == "rollback") transaction.Rollback();
        else transaction.Dispose();
        foreach (var call in new Action[]
        {
            () => transaction.DatabaseAccess.ExecuteScalar<int>("SELECT value"),
            () => transaction.DatabaseAccess.ExecuteNonQuery("UPDATE rows"),
            () => transaction.DatabaseAccess.ExecuteReader("SELECT rows"),
            () => rows.MoveNext()
        })
        {
            if (completion == "dispose") _ = Capture<ObjectDisposedException>(call);
            else _ = Capture<InvalidOperationException>(call);
        }
        await Assert.That(factory.Bindings).IsEqualTo(0);
    }

    [Test]
    [Arguments(ExecutionEffects.NoStatement)]
    [Arguments(ExecutionEffects.OrdinaryRead)]
    [Arguments(ExecutionEffects.Mutation)]
    [Arguments(ExecutionEffects.Initialization)]
    [Arguments(ExecutionEffects.Unknown)]
    internal async Task SyncRawCommands_PostDispatchClaimsNeverGrantReuse(ExecutionEffects effects)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        factory.Evidence = new(ExecutionFailureCause.ProviderError, effects, TransactionIntegrity.Confirmed, RollbackAvailable: true);
        var expected = new Exception("raw dispatch");
        factory.ExecutionFailure = expected;
        using var command = new ControlledCommand();
        await Assert.That(Capture<Exception>(() => transaction.DatabaseAccess.ExecuteNonQuery(command))).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(effects == ExecutionEffects.Initialization
            ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(transaction.Commit);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_HelperWaitsForInFlightSynchronousAcquisitionOrAdvance(bool acquisition)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        fixture.Scenario.AsyncCompletion = new();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Pause()
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
        }
        var native = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        factory.Reader = () => native;
        if (acquisition) factory.Executing = Pause; else native.OnRead = Pause;
        Task<IDataLinqDataReader>? unfinished = null;
        var helper = transaction.RunCallbackAsyncCore(async token =>
        {
            // Test-only concurrency drives a genuinely blocking synchronous adapter.
            unfinished = Task.Factory.StartNew(() =>
            {
                var reader = transaction.DatabaseAccess.ExecuteReader("SELECT rows");
                if (!acquisition) reader.ReadNextRow();
                return reader;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await entered.Task;
            return 9;
        }, new());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(helper.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(factory.CommandDisposals).IsEqualTo(0);
        }
        finally { release.Set(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        var escaped = await unfinished!;
        escaped.Dispose();
        await Assert.That(native.Disposals).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task SyncRawCommands_HelperDrainCollectsEagerFailureBeforeCompletion()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new Exception("unfinished scalar");
        factory.Executing = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            throw expected;
        };
        var helper = transaction.ExecutionGate.BeginHelperLifetime();
        var unfinished = Task.Factory.StartNew(() => transaction.DatabaseAccess.ExecuteScalar("SELECT value"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var failures = new ExecutionFailures();
        Task<TransactionOperationGate.Lease>? drain = null;
        Exception? observed = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Observe admission closure before releasing the blocked call. This cannot
            // accidentally test a failure that happened while the callback was still open.
            drain = helper.CloseAndDrainAsync(failures);
            await Assert.That(drain.IsCompleted).IsFalse();
        }
        finally
        {
            release.Set();
            try { await unfinished; }
            catch (Exception failure) { observed = failure; }
            using var completionOwner = await (drain ?? helper.CloseAndDrainAsync(failures)).WaitAsync(TimeSpan.FromSeconds(10));
            transaction.DatabaseAccess.Dispose();
        }
        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(failures.Primary).IsTypeOf<InvalidOperationException>();
        var context = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, transaction.TransactionID);
        await Assert.That(context.SecondaryFailures.Any(x => ReferenceEquals(x.Exception, expected))).IsTrue();
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_PreserveOptionalOwnedBinaryCapabilityAndPositionGuard(bool supportsOwnership)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var native = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        var binary = new SyncRawBinaryProbe(native);
        factory.Reader = () => supportsOwnership ? binary : native;
        using var reader = transaction.DatabaseAccess.ExecuteReader("SELECT rows");
        await Assert.That(reader is IDataLinqOwnedBinaryBufferReader).IsEqualTo(supportsOwnership);
        if (supportsOwnership)
        {
            var owned = (IDataLinqOwnedBinaryBufferReader)reader;
            _ = Capture<InvalidOperationException>(() => owned.TakeOwnedBytes(0));
            reader.ReadNextRow();
            await Assert.That(owned.TakeOwnedBytes(0)).IsSameReferenceAs(binary.Value);
            reader.Dispose();
            _ = Capture<ObjectDisposedException>(() => owned.TakeOwnedBytes(0));
        }
    }

    [Test]
    public async Task SyncRawCommands_StandaloneReadersHaveIndependentLifetimesAndNoTransactionRecovery()
    {
        var factory = new SyncRawTestFactory { Reader = () => new ControlledAsyncDataReader { AllowSynchronousCalls = true } };
        var access = new SyncRawStandaloneTestAccess(factory);
        using var first = access.ExecuteReader("SELECT rows");
        using var second = access.ExecuteReader("SELECT rows");
        await Assert.That(first.ReadNextRow()).IsTrue();
        await Assert.That(second.ReadNextRow()).IsTrue();
        await Assert.That(access.ExecuteScalar<int>("SELECT value")).IsEqualTo(7);
        first.Dispose();
        await Assert.That(second.ReadNextRow()).IsTrue();
        var expected = new Exception("standalone");
        factory.ExecutionFailure = expected;
        await Assert.That(Capture<Exception>(() => access.ExecuteNonQuery("UPDATE rows"))).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        second.Dispose();
        await Assert.That(factory.CommandDisposals).IsEqualTo(4);
    }

    private sealed class SyncRawBinaryProbe(IDataLinqDataReader reader)
        : OwnedCommandDataReader(reader, new ScriptedDbCommand()), IDataLinqOwnedBinaryBufferReader
    {
        internal byte[] Value { get; } = [1, 2, 3];
        public byte[] TakeOwnedBytes(int ordinal) => Value;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_CompetingInvocationCannotDisposeWinningCommand(bool readerInvocation)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new SyncRawTestFactory
        {
            Executing = () =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            }
        };
        var command = factory.BindCommand("one captured invocation");
        void Execute()
        {
            if (readerInvocation) { using var reader = SyncRawDataReader.Open(command, null); }
            else _ = SyncRawExecution.Execute(command, SyncCommandKind.Scalar, null,
                static (invocation, owner) => invocation.ExecuteScalar(owner), static value => value);
        }
        var winning = Task.Factory.StartNew(Execute, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(Execute);
            await Assert.That(factory.CommandDisposals).IsEqualTo(0);
            await Assert.That(factory.Executions.Count).IsEqualTo(1);
        }
        finally { release.Set(); await winning; }
        _ = Capture<InvalidOperationException>(Execute);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
    }

    private sealed class SyncRawStandaloneTestAccess(ISyncRawCommandFactory factory) : DatabaseAccess, ISyncRawCommandFactory
    {
        public override IDataLinqDataReader ExecuteReader(string query) => ExecuteReaderSyncCore(query);
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => ExecuteReaderSyncCore(command);
        public override object? ExecuteScalar(string query) => ExecuteScalarSyncCore(query);
        public override object? ExecuteScalar(IDbCommand command) => ExecuteScalarSyncCore(command);
        public override T ExecuteScalar<T>(string query) => ExecuteScalarSyncCore<T>(query);
        public override T ExecuteScalar<T>(IDbCommand command) => ExecuteScalarSyncCore<T>(command);
        public override int ExecuteNonQuery(string query) => ExecuteNonQuerySyncCore(query);
        public override int ExecuteNonQuery(IDbCommand command) => ExecuteNonQuerySyncCore(command);
        SyncRawCommand ISyncRawCommandFactory.BindCommand(string sql) => factory.BindCommand(sql);
        SyncRawCommand ISyncRawCommandFactory.BindCommand(IDbCommand command) => factory.BindCommand(command);
        SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(string sql) => factory.BindScalar<T>(sql);
        SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(IDbCommand command) => factory.BindScalar<T>(command);
    }
}
