using System;
using System.Collections.Generic;
using System.Data;
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
    [Test]
    [Arguments("non-query", false)]
    [Arguments("non-query", true)]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("typed", false)]
    [Arguments("typed", true)]
    public async Task SyncRawCommands_PublicEagerCallsHoldAdmissionThroughCleanupAndConversion(string family, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        fixture.PrimeCommittedRow(1, "old");
        var factory = EnableSyncRaw(fixture);
        var callbacks = 0;
        void CheckBusy()
        {
            callbacks++;
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteScalar("raw reentry"));
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        factory.Executing = CheckBusy;
        factory.CommandDisposing = CheckBusy;
        factory.Converting = CheckBusy;
        using var command = new ControlledCommand { CommandText = "UPDATE rows RETURNING value", CommandTimeout = 23 };
        await Assert.That(ExecuteSyncRawTest(transaction.DatabaseAccess, family, borrowed ? command : null)).IsEqualTo(7);
        await Assert.That(callbacks >= (borrowed ? 1 : 2)).IsTrue();
        await Assert.That(factory.Executions.Count).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 1);
        if (borrowed) await Assert.That(factory.Executions[0].Command).IsSameReferenceAs(command);
        await Assert.That(command.CommandText).IsEqualTo("UPDATE rows RETURNING value");
        await Assert.That(command.CommandTimeout).IsEqualTo(23);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        transaction.Commit();
        await Assert.That(fixture.Database.Get<TransactionMutationGuardRow, int>(1)!.Value).IsEqualTo("old");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_ReaderRetainsAdmissionThroughEofAndOwnedCleanup(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var native = new ControlledAsyncDataReader { AllowSynchronousCalls = true };
        factory.Reader = () => native;
        factory.CommandDisposing = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteNonQuery("during cleanup"));
        };
        using var command = new ControlledCommand();
        var reader = borrowed ? transaction.DatabaseAccess.ExecuteReader(command) : transaction.DatabaseAccess.ExecuteReader("SELECT rows");
        await Assert.That(reader.GetOrdinal("Value")).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => reader.GetInt32(0));
        await Assert.That(reader.ReadNextRow()).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(11);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(transaction.Rollback);
        _ = Capture<InvalidOperationException>(() => transaction.Delete(fixture.CreateImmutable(2, "x")));
        await Assert.That(reader.ReadNextRow()).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(22);
        await Assert.That(reader.ReadNextRow()).IsFalse();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(() => reader.GetInt32(0));
        reader.Dispose();
        reader.Dispose();
        await Assert.That(native.SyncCalls).IsEqualTo(4);
        await Assert.That(native.AsyncDisposeCalls).IsEqualTo(0);
        await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = Capture<ObjectDisposedException>(() => reader.GetInt32(0));
        _ = transaction.Query();
    }

    [Test]
    [Arguments("create")]
    [Arguments("validate-created")]
    [Arguments("dispatch")]
    [Arguments("row")]
    [Arguments("reader-cleanup")]
    [Arguments("command-cleanup")]
    [Arguments("convert")]
    public async Task SyncRawCommands_FailureClassificationDistinguishesPreDispatchAndRawEffects(string stage)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "pending"));
        var factory = EnableSyncRaw(fixture);
        var expected = new InjectedMutationException(stage);
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        factory.Reader = () => reader;
        if (stage == "create") factory.CreateFailure = expected;
        if (stage == "validate-created") factory.CommandValidationFailure = expected;
        if (stage == "dispatch") factory.ExecutionFailure = expected;
        if (stage == "row") reader.OnRead = () => throw expected;
        if (stage == "reader-cleanup") reader.DisposeFailure = expected;
        if (stage == "command-cleanup") factory.CommandDisposing = () => throw expected;
        if (stage == "convert") factory.Converting = () => throw expected;
        var failure = Capture<InjectedMutationException>(() =>
        {
            if (stage is "row" or "reader-cleanup")
            {
                using var rows = transaction.DatabaseAccess.ExecuteReader("UPDATE rows RETURNING value");
                rows.ReadNextRow();
            }
            else transaction.DatabaseAccess.ExecuteScalar<int>("UPDATE rows RETURNING value");
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        if (stage is "create" or "validate-created")
        {
            await Assert.That((context.Recovery & ExecutionRecoveryActions.Continue) != 0).IsTrue();
            await Assert.That(factory.Executions).IsEmpty();
            transaction.Commit();
        }
        else
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Commit);
            if (stage is "reader-cleanup" or "command-cleanup")
            {
                await Assert.That(context.HasCleanupFailure).IsTrue();
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
            }
            else
            {
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
                transaction.Rollback();
            }
        }
        await Assert.That(factory.CommandDisposals).IsEqualTo(stage == "create" ? 0 : 1);
        using (transaction.ExecutionGate.Enter("verify lease released")) { }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_OrderedCleanupEvidenceDoesNotReplacePrimary(bool sameException)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var primary = new Exception("read");
        var readerCleanup = sameException ? primary : new Exception("reader cleanup");
        var commandCleanup = sameException ? primary : new Exception("command cleanup");
        var assessment = new Exception("assessment");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"))
        { OnRead = () => throw primary, DisposeFailure = readerCleanup };
        factory.Reader = () => reader;
        factory.CommandDisposing = () => throw commandCleanup;
        factory.EvidenceFailure = assessment;
        using var returned = transaction.DatabaseAccess.ExecuteReader("SELECT rows");
        await Assert.That(Capture<Exception>(() => returned.ReadNextRow())).IsSameReferenceAs(primary);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        var secondary = context.SecondaryFailures.Select(x => x.Exception).ToArray();
        await Assert.That(secondary.Length).IsEqualTo(sameException ? 1 : 3);
        if (!sameException)
        {
            await Assert.That(secondary[0]).IsSameReferenceAs(readerCleanup);
            await Assert.That(secondary[1]).IsSameReferenceAs(commandCleanup);
        }
        await Assert.That(secondary[^1]).IsSameReferenceAs(assessment);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_SequencesAreColdRepeatableAndYieldEphemeralPositions(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        factory.Reader = () => new ControlledAsyncDataReader { AllowSynchronousCalls = true };
        using var command = new ControlledCommand();
        var rows = borrowed ? transaction.DatabaseAccess.ReadReaderSyncCore(command)
            : transaction.DatabaseAccess.ReadReaderSyncCore("SELECT rows");
        using (rows.GetEnumerator()) { }
        await Assert.That(factory.Bindings).IsEqualTo(0);
        using (var iterator = rows.GetEnumerator())
        {
            await Assert.That(iterator.MoveNext()).IsTrue();
            var first = iterator.Current;
            await Assert.That(first.GetInt32(0)).IsEqualTo(11);
            await Assert.That(iterator.MoveNext()).IsTrue();
            await Assert.That(iterator.Current).IsSameReferenceAs(first);
            await Assert.That(first.GetInt32(0)).IsEqualTo(22);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        await Assert.That(rows.Count()).IsEqualTo(2);
        await Assert.That(factory.Executions.Count).IsEqualTo(2);
        await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 2);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = transaction.Query();
    }

    [Test]
    public async Task SyncRawCommands_ManagedMutationAndRawModelsUsePrivateDispatchWithoutReentry()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        factory.Reader = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        factory.Executing = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteNonQuery("reentrant raw"));
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        };
        var mutable = fixture.CreateExistingMutable(401, "before");
        mutable["Value"] = "stored";
        var saved = transaction.Update(mutable);
        await Assert.That(saved.GetReadSource()).IsSameReferenceAs(transaction);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(factory.Executions.Select(x => x.Kind).ToArray()).IsEquivalentTo(new[] { SyncCommandKind.NonQuery, SyncCommandKind.Reader });
        var raw = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").Single();
        await Assert.That(raw.GetReadSource()).IsSameReferenceAs(transaction);
        await Assert.That(raw.Id).IsEqualTo(401);
        transaction.Rollback();
    }

    [Test]
    public async Task SyncRawCommands_SyncAndAsyncPublicExecutionShareAdmission()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var asyncAccess = new ControlledAsyncDatabaseAccess(new(paused: true));
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Access = asyncAccess };
        var pending = transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows");
        await asyncAccess.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteScalar("SELECT value"));
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteReader("SELECT rows"));
            await Assert.That(factory.Bindings).IsEqualTo(0);
        }
        finally { asyncAccess.Dispatch.Release(); }
        await pending;
        using (var reader = transaction.DatabaseAccess.ExecuteReader("SELECT rows"))
            await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT value"))).IsTypeOf<InvalidOperationException>();
        await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT value");
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_LazyInitializationUsesSharedStateAndDirectSynchronousCalls(bool fails)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var expected = new Exception("initialize");
        var resource = new ControlledTransactionResource { SyncInitializationFailure = fails ? expected : null };
        var lazy = BindInitialization(fixture, transaction, resource);
        factory.Initialization = lazy;
        resource.Initialized = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteNonQuery("reentrant initialization"));
        };
        if (fails)
        {
            await Assert.That(Capture<Exception>(() => transaction.DatabaseAccess.ExecuteScalar("SELECT value"))).IsSameReferenceAs(expected);
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
            await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteScalar("retry"));
            await Assert.That(factory.Executions).IsEmpty();
            await Assert.That(resource.Calls).IsEquivalentTo(new[] { "sync-initialize", "sync-dispose" });
        }
        else
        {
            transaction.DatabaseAccess.ExecuteScalar("SELECT value");
            fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Initialization = lazy };
            await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT value");
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
            await Assert.That(resource.Calls).IsEquivalentTo(new[] { "sync-initialize" });
        }
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_AsyncHelperDrainsEscapedSynchronousReader(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        fixture.Scenario.AsyncCompletion = new();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        factory.Reader = () => reader;
        using var command = new ControlledCommand();
        IDataLinqDataReader? escaped = null;
        var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(token =>
        {
            escaped = borrowed ? transaction.DatabaseAccess.ExecuteReader(command) : transaction.DatabaseAccess.ExecuteReader("SELECT rows");
            escaped.ReadNextRow();
            return Task.FromResult(9);
        }, new()));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 1);
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        _ = Capture<InvalidOperationException>(() => escaped!.ReadNextRow());
        escaped!.Dispose();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncRawCommands_ConcurrentReaderCallsCannotReleaseActiveOwnership(bool duringRead)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Pause()
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
        }
        var native = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        factory.Reader = () => native;
        using var reader = transaction.DatabaseAccess.ExecuteReader("SELECT rows");
        if (duringRead) native.OnRead = Pause; else native.OnDispose = Pause;
        var active = Task.Factory.StartNew(() => { if (duringRead) reader.ReadNextRow(); else reader.Dispose(); },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            _ = Capture<InvalidOperationException>(() => reader.ReadNextRow());
            _ = Capture<InvalidOperationException>(() => reader.GetInt32(0));
            _ = Capture<InvalidOperationException>(reader.Dispose);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(factory.CommandDisposals).IsEqualTo(0);
        }
        finally { release.Set(); await active; }
        native.OnRead = null;
        native.OnDispose = null;
        reader.Dispose();
        await Assert.That(native.Disposals).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    private static int ExecuteSyncRawTest(DatabaseAccess access, string family, IDbCommand? command)
        => family switch
        {
            "non-query" => command is null ? access.ExecuteNonQuery("UPDATE rows") : access.ExecuteNonQuery(command),
            "scalar" => (int)(command is null ? access.ExecuteScalar("SELECT value")! : access.ExecuteScalar(command)!),
            "typed" => command is null ? access.ExecuteScalar<int>("SELECT value") : access.ExecuteScalar<int>(command),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

    private static SyncRawTestFactory EnableSyncRaw(ScriptedFixture fixture)
    {
        var factory = new SyncRawTestFactory();
        fixture.Scenario.SyncCommands = factory;
        return factory;
    }

    private sealed partial class ScriptedDatabaseTransaction : ISyncRawCommandFactory
    {
        private ISyncRawCommandFactory SyncRaw => scenario.SyncCommands ?? throw new NotSupportedException("Scripted synchronous raw binding was not enabled.");
        SyncRawCommand ISyncRawCommandFactory.BindCommand(string sql) => SyncRaw.BindCommand(sql);
        SyncRawCommand ISyncRawCommandFactory.BindCommand(IDbCommand command) => SyncRaw.BindCommand(command);
        SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(string sql) => SyncRaw.BindScalar<T>(sql);
        SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(IDbCommand command) => SyncRaw.BindScalar<T>(command);

        private IDataLinqDataReader ExecuteOwnedSyncTestReader(string sql, TransactionOperationGate.Step owner)
        {
            var command = new ScriptedDbCommand { CommandText = sql };
            try { return OwnedCommandDataReader.Create(BindOwnedSyncTestCommand(command, SyncCommandKind.Reader).ExecuteReader(owner), command); }
            catch { command.Dispose(); throw; }
        }

        private SyncRawCommand BindOwnedSyncTestCommand(IDbCommand command, SyncCommandKind kind)
        {
            var bound = SyncRaw.BindCommand(command);
            bound.Reserve(kind, hasOwner: true);
            return bound;
        }
    }

    private sealed class SyncRawTestFactory : ISyncRawCommandFactory, ISyncCommandAccess, IAsyncReadFailureEvidence
    {
        internal ISyncCommandInitialization? Initialization { get; set; }
        internal Action? Executing { get; set; }
        internal Action? Converting { get; set; }
        internal Action? CommandDisposing { get; set; }
        internal Exception? CreateFailure { get; set; }
        internal Exception? CommandValidationFailure { get; set; }
        internal Exception? ExecutionFailure { get; set; }
        internal Exception? EvidenceFailure { get; set; }
        internal Func<IDataLinqDataReader> Reader { get; set; } = () => EmptyReader.Instance;
        internal List<(SyncCommandKind Kind, IDbCommand Command)> Executions { get; } = [];
        internal int Bindings { get; private set; }
        internal int CommandDisposals { get; private set; }
        internal ReadFailureEvidence Evidence { get; set; } = new(ExecutionFailureCause.ProviderError,
            ExecutionEffects.OrdinaryRead, TransactionIntegrity.Confirmed, RollbackAvailable: true);

        public SyncRawCommand BindCommand(string sql)
        {
            Bindings++;
            return new(this, () =>
            {
                if (CreateFailure is not null) throw CreateFailure;
                return new ScriptedDbCommand(() => { CommandDisposals++; CommandDisposing?.Invoke(); }) { CommandText = sql };
            }, initialization: Initialization);
        }
        public SyncRawCommand BindCommand(IDbCommand command)
        {
            Bindings++;
            return new(this, command, Initialization);
        }
        public SyncRawScalarInvocation<T> BindScalar<T>(string sql) => new(BindCommand(sql), ConvertScalar<T>);
        public SyncRawScalarInvocation<T> BindScalar<T>(IDbCommand command) => new(BindCommand(command), ConvertScalar<T>);
        private T ConvertScalar<T>(object? value) { Converting?.Invoke(); return (T)Convert.ChangeType(value!, typeof(T)); }
        public void ValidateCommand(IDbCommand command, SyncCommandKind kind)
        {
            if (CommandValidationFailure is not null) throw CommandValidationFailure;
        }
        private void Execute(IDbCommand command, SyncCommandKind kind)
        {
            Executions.Add((kind, command));
            Executing?.Invoke();
            if (ExecutionFailure is not null) throw ExecutionFailure;
        }
        public IDataLinqDataReader ExecuteReader(IDbCommand command) { Execute(command, SyncCommandKind.Reader); return Reader(); }
        public object? ExecuteScalar(IDbCommand command) { Execute(command, SyncCommandKind.Scalar); return 7; }
        public int ExecuteNonQuery(IDbCommand command) { Execute(command, SyncCommandKind.NonQuery); return 7; }
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
            => EvidenceFailure is { } assessment ? throw assessment : Evidence;
    }
}
