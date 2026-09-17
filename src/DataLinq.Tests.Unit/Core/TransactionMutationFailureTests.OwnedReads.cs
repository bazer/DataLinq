using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnedHydration_PreservesTransactionIdentityWithoutLeakingPermission(bool legacyKey)
    {
        using var fixture = new ScriptedFixture();
        // Keep this identity assertion independent of age-maintenance publication races.
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        var callbacks = 0;
        var table = legacyKey ? fixture.BinaryTable : fixture.RowTable;
        IRowData data = legacyKey
            ? new ScriptedBinaryRowData(table, [1, 2], [3, 4])
            : new ScriptedRowData(table, 401, "stored");
        var reader = new OwnedReadProbe(data);
        fixture.Scenario.ReaderFactory = () => reader;
        void CheckBusy()
        {
            callbacks++;
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(401)));
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Rollback);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        fixture.Scenario.CommandCreated = () =>
        {
            if (fixture.Scenario.CommandCreations == 2)
                CheckBusy();
        };
        reader.OnRead = CheckBusy;
        reader.OnDispose = CheckBusy;
        fixture.Scenario.CommandDisposed = CheckBusy;

        IImmutableInstance saved;
        if (legacyKey)
        {
            var mutable = fixture.CreateExistingBinaryMutable([1, 2], [9]);
            mutable["Payload"] = new byte[] { 3, 4 };
            saved = transaction.Update(mutable);
        }
        else
        {
            var mutable = fixture.CreateExistingMutable(401, "before");
            mutable["Value"] = "stored";
            saved = transaction.Update(mutable);
        }

        await Assert.That(callbacks >= 5).IsTrue();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(2);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(saved.GetReadSource()).IsSameReferenceAs(transaction);
        var cached = fixture.Provider.GetTableCache(table).GetRow(saved.PrimaryKeys(), transaction);
        await Assert.That(cached).IsSameReferenceAs(saved);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(1);
        transaction.Commit();
        await Assert.That(saved.GetReadSource()).IsSameReferenceAs(fixture.Provider.ReadOnlyAccess);
    }

    [Test]
    public async Task OwnedSingleRowRead_CoversColdAndWarmCacheAndRejectsStaleOrForeignDispatch()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        var key = DataLinqKey.FromValue(401);
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        var first = transaction.Get<TransactionMutationGuardRow>(key)!;
        await Assert.That(transaction.Get<TransactionMutationGuardRow>(key)).IsSameReferenceAs(first);
        await Assert.That(first.GetReadSource()).IsSameReferenceAs(transaction);
        var before = fixture.Scenario.SnapshotCounts();
        IDataLinqSourceRowServices services;
        TransactionOperationGate.Step stale;
        using (var lease = transaction.ExecutionGate.Enter("owned test"))
        using (var step = transaction.ExecutionGate.EnterStep(lease))
        {
            stale = step;
            services = transaction.GetOwnedRowServices(step);
            await Assert.That(fixture.RowCache.GetOwnedRow(key, transaction, step)).IsSameReferenceAs(first);
            _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(key));
            _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(999)));
            _ = Capture<InvalidOperationException>(() => fixture.RowCache.GetOwnedRow(key, other, step));
            _ = Capture<InvalidOperationException>(() => first.GetReadSource());
        }
        using (var newer = transaction.ExecutionGate.Enter("new owner"))
        using (var newerStep = transaction.ExecutionGate.EnterStep(newer))
        {
            _ = Capture<InvalidOperationException>(() => fixture.RowCache.GetOwnedRow(key, transaction, stale));
            _ = Capture<InvalidOperationException>(() => services.RowLoader.LoadSingle(fixture.RowTable, key));
            stale.Dispose();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(before);
        await Assert.That(transaction.Get<TransactionMutationGuardRow>(key)).IsSameReferenceAs(first);
    }

    [Test]
    [Arguments(false, "early")]
    [Arguments(true, "early")]
    [Arguments(false, "end")]
    [Arguments(true, "read-failure")]
    [Arguments(false, "cleanup-failure")]
    public async Task RawReadEnumeration_OwnsSlotBetweenRowsAndThroughCleanup(bool callerCommand, string exit)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        fixture.Scenario.ReaderFactory = () => reader;
        var callerCommandDisposals = 0;
        using var command = new ScriptedDbCommand(() => callerCommandDisposals++);
        var sequence = callerCommand
            ? transaction.GetFromCommand<TransactionMutationGuardRow>(command)
            : transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows");
        using var rows = sequence.GetEnumerator();
        // Construction is cold; admission starts on the first MoveNext.
        using (transaction.ExecutionGate.Enter("before enumeration")) { }
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
        await Assert.That(rows.MoveNext()).IsTrue();
        var row = rows.Current;
        _ = Capture<InvalidOperationException>(transaction.Commit);
        _ = Capture<InvalidOperationException>(transaction.Dispose);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        var cleanupChecked = false;
        reader.OnDispose = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            cleanupChecked = true;
        };
        if (exit == "read-failure")
        {
            var expected = new InjectedMutationException("reader move");
            reader.OnRead = () => throw expected;
            await Assert.That(Capture<InjectedMutationException>(() => rows.MoveNext())).IsSameReferenceAs(expected);
        }
        else if (exit == "cleanup-failure")
        {
            var expected = new InjectedMutationException("reader cleanup");
            reader.DisposeFailure = expected;
            await Assert.That(Capture<InjectedMutationException>(rows.Dispose)).IsSameReferenceAs(expected);
        }
        else if (exit == "end")
            await Assert.That(rows.MoveNext()).IsFalse();
        else
            rows.Dispose();

        await Assert.That(cleanupChecked).IsTrue();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(callerCommandDisposals).IsEqualTo(0);
        await Assert.That(transaction.IsDisposed).IsFalse();
        // Gate release is independent of provider trust. Recovery classification after
        // failed cleanup belongs to W1.3; this test must not certify connection reuse.
        using (transaction.ExecutionGate.Enter("verify released slot")) { }
        if (exit != "cleanup-failure")
        {
            await Assert.That(row.GetReadSource()).IsSameReferenceAs(transaction);
            await Assert.That(transaction.IsPoisoned).IsFalse();
            _ = transaction.Query();
        }
    }

    [Test]
    [Arguments("single")]
    [Arguments("batch")]
    [Arguments("index")]
    [Arguments("first")]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("scalar-object")]
    public async Task SourceReadBoundaries_HoldAdmissionThroughProviderAndCommandCleanup(string boundary)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var key = DataLinqKey.FromValue(401);
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var callbacks = 0;
        void CheckBusy()
        {
            callbacks++;
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        fixture.Scenario.CommandCreated = CheckBusy;
        fixture.Scenario.CommandDisposed = CheckBusy;
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        reader.OnRead = CheckBusy;
        reader.OnDispose = CheckBusy;
        fixture.Scenario.ReaderFactory = () => reader;
        var loader = new DataSourceAccessSourceRowLoader(transaction);
        switch (boundary)
        {
            case "single": _ = loader.LoadSingle(fixture.RowTable, key); break;
            case "batch": _ = loader.Load(new SourcePrimaryKeyRowRequest(fixture.RowTable, [key])); break;
            case "index":
                var index = fixture.RowTable.ColumnIndices.Single(x => x.Name == "idx_transaction_mutation_guard_value");
                _ = loader.Load(new SourceIndexRowRequest(fixture.RowTable, index, DataLinqKey.FromValue("stored")));
                break;
            case "first": _ = select.ReadFirstRow(); break;
            case "reader": _ = select.ReadRows().ToArray(); break;
            case "scalar": _ = select.ExecuteScalar<int>(); break;
            case "scalar-object": _ = select.ExecuteScalar(); break;
        }
        await Assert.That(callbacks >= 2).IsTrue();
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    public async Task SourceRead_PreCancellationAndCreationFailureLeaveTransactionReusable()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var loader = new DataSourceAccessSourceRowLoader(transaction);
        var key = DataLinqKey.FromValue(401);
        _ = Capture<OperationCanceledException>(() => loader.LoadSingle(fixture.RowTable, key, new CancellationToken(true)));
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        var expected = new InjectedMutationException("read command creation");
        fixture.Scenario.CommandFailure = expected;
        await Assert.That(Capture<InjectedMutationException>(() => loader.LoadSingle(fixture.RowTable, key))).IsSameReferenceAs(expected);
        fixture.Scenario.CommandFailure = null;
        await Assert.That(loader.LoadSingle(fixture.RowTable, key)).IsNull();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        _ = transaction.Query();
    }

    [Test]
    public async Task SuspendedReadCleanup_RejectsOtherThreadsUntilDisposalCompletes()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var release = new ManualResetEventSlim();
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        fixture.Scenario.ReaderFactory = () => reader;
        reader.OnDispose = () =>
        {
            cleanupStarted.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Test did not release reader cleanup.");
        };
        var lookup = Task.Run(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(401)));
        try
        {
            await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var before = fixture.Scenario.SnapshotCounts();
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(999)));
            await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(before);
            await Assert.That(transaction.IsDisposed).IsFalse();
        }
        finally
        {
            release.Set();
            await lookup;
        }
        await Assert.That((await lookup)!.GetReadSource()).IsSameReferenceAs(transaction);
        await Assert.That(reader.Disposals).IsEqualTo(1);
    }

    private sealed class OwnedReadProbe(IRowData row) : IDataLinqDataReader
    {
        private int moves;
        internal Action? OnRead { get; set; }
        internal Action? OnDispose { get; set; }
        internal Exception? DisposeFailure { get; set; }
        internal int Disposals { get; private set; }
        public bool ReadNextRow() { OnRead?.Invoke(); return moves++ == 0; }
        public void Dispose()
        {
            Disposals++;
            OnDispose?.Invoke();
            if (DisposeFailure is not null) throw DisposeFailure;
        }
        public object GetValue(int ordinal) => row[ordinal]!;
        public int GetOrdinal(string name) => row.Table.GetColumnByDbName(name).Index;
        public string GetString(int ordinal) => (string)row[ordinal]!;
        public bool GetBoolean(int ordinal) => (bool)row[ordinal]!;
        public int GetInt32(int ordinal) => (int)row[ordinal]!;
        public DateOnly GetDateOnly(int ordinal) => (DateOnly)row[ordinal]!;
        public Guid GetGuid(int ordinal) => (Guid)row[ordinal]!;
        public byte[]? GetBytes(int ordinal) => (byte[]?)row[ordinal];
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public T? GetValue<T>(ColumnDefinition column) => (T?)row[column];
        public T? GetValue<T>(ColumnDefinition column, int ordinal) => (T?)row[ordinal];
        public bool IsDbNull(int ordinal) => row[ordinal] is null or DBNull;
    }
}
