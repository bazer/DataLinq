using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("single")]
    [Arguments("batch")]
    [Arguments("index")]
    [Arguments("first")]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("scalar-object")]
    [Arguments("scalar-cache")]
    [Arguments("lookup")]
    [Arguments("linq")]
    [Arguments("raw-string")]
    [Arguments("raw-command")]
    public async Task SyncOwnedCommands_ReadPipelinesCarryPrivateAdmissionThroughCleanup(string route)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        var owners = new List<TransactionOperationGate.Step>();
        var callbacks = 0;
        var readers = new List<OwnedReadProbe>();
        void CheckBusy()
        {
            callbacks++;
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteScalar("reentrant raw"));
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            _ = other.Query();
        }
        fixture.Scenario.SyncPublicDispatch = _ => transaction.EnsureCanRead("raw test command");
        fixture.Scenario.SyncOwnedDispatch = (_, step) =>
        {
            transaction.ExecutionGate.ValidateStep(step);
            owners.Add(step);
            CheckBusy();
        };
        fixture.Scenario.CommandCreated = CheckBusy;
        fixture.Scenario.CommandDisposed = CheckBusy;
        fixture.Scenario.ScalarExecuting = CheckBusy;
        fixture.Scenario.ReaderFactory = () =>
        {
            var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
            { OnRead = CheckBusy, OnDispose = CheckBusy };
            readers.Add(reader);
            return reader;
        };
        var key = DataLinqKey.FromValue(401);
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var loader = new DataSourceAccessSourceRowLoader(transaction);
        var borrowedDisposals = 0;
        using var borrowed = new ScriptedDbCommand(() => borrowedDisposals++);
        switch (route)
        {
            case "single": _ = loader.LoadSingle(fixture.RowTable, key); break;
            case "batch": _ = loader.Load(new SourcePrimaryKeyRowRequest(fixture.RowTable, [key])); break;
            case "index":
                var index = fixture.RowTable.ColumnIndices.Single(x => x.Name == "idx_transaction_mutation_guard_value");
                _ = loader.Load(new SourceIndexRowRequest(fixture.RowTable, index, DataLinqKey.FromValue("stored")));
                break;
            case "first": _ = select.ReadFirstRow(); break;
            case "reader": _ = select.ReadRows().ToArray(); break;
            case "scalar": await Assert.That(select.ExecuteScalar<int>()).IsEqualTo(1); break;
            case "scalar-object": await Assert.That(select.ExecuteScalar()).IsEqualTo(1L); break;
            case "scalar-cache":
                _ = new ScalarColumnRowsQuery(fixture.RowTable, transaction, fixture.RowTable.GetColumnByDbName("id"), 401).ReadFirstRow();
                break;
            case "lookup": _ = transaction.Get<TransactionMutationGuardRow>(key); break;
            case "linq": _ = transaction.Query().Rows.Where(row => row.Id == 401).ToArray(); break;
            case "raw-string": _ = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").ToArray(); break;
            case "raw-command": _ = transaction.GetFromCommand<TransactionMutationGuardRow>(borrowed).ToArray(); break;
        }
        await Assert.That(owners.Count > 0).IsTrue();
        await Assert.That(callbacks > 1).IsTrue();
        await Assert.That(readers.All(reader => reader.Disposals == 1)).IsTrue();
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(fixture.Scenario.CommandCreations);
        await Assert.That(borrowedDisposals).IsEqualTo(0);
        foreach (var owner in owners)
            _ = Capture<InvalidOperationException>(() => transaction.ExecutionGate.ValidateStep(owner));
        _ = transaction.Query();
        fixture.Scenario.ScalarExecuting = null;
        await Assert.That(transaction.DatabaseAccess.ExecuteScalar("raw after release")).IsEqualTo(1L);
    }

    [Test]
    [Arguments("reader")]
    [Arguments("reader-string")]
    [Arguments("scalar")]
    [Arguments("typed-scalar")]
    [Arguments("non-query")]
    public async Task SyncOwnedCommands_RejectForeignRetiredAndUnboundAuthorityBeforeDispatch(string family)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        using var command = new ScriptedDbCommand();
        var dispatched = 0;
        fixture.Scenario.SyncOwnedDispatch = (_, _) => dispatched++;
        TransactionOperationGate.Step retired;
        using (var lease = transaction.ExecutionGate.Enter("original"))
        using (var step = transaction.ExecutionGate.EnterStep(lease))
        {
            retired = step;
            _ = Capture<InvalidOperationException>(() => ExecuteSyncOwnedTestFamily(other.DatabaseAccess, family, command, step));
            _ = Capture<InvalidOperationException>(() => ExecuteSyncOwnedTestFamily(fixture.Provider.ReadOnlyAccess.DatabaseAccess, family, command, step));
        }
        using (var lease = transaction.ExecutionGate.Enter("replacement"))
        using (var step = transaction.ExecutionGate.EnterStep(lease))
        {
            _ = Capture<InvalidOperationException>(() => ExecuteSyncOwnedTestFamily(transaction.DatabaseAccess, family, command, retired));
            transaction.ExecutionGate.ValidateStep(step);
            await Assert.That(dispatched).IsEqualTo(0);
            ExecuteSyncOwnedTestFamily(transaction.DatabaseAccess, family, command, step);
            await Assert.That(dispatched).IsEqualTo(1);
        }
        _ = transaction.Query();
    }

    [Test]
    [Arguments("reader")]
    [Arguments("reader-string")]
    [Arguments("scalar")]
    [Arguments("typed-scalar")]
    [Arguments("non-query")]
    public async Task SyncOwnedCommands_DefaultHooksPreservePublicOverridesAndBorrowedCommands(string family)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var calls = 0;
        var disposals = 0;
        using var command = new ScriptedDbCommand(() => disposals++);
        using (var lease = transaction.ExecutionGate.Enter("legacy custom adapter"))
        using (var step = transaction.ExecutionGate.EnterStep(lease))
        {
            fixture.Scenario.SyncPublicDispatch = value =>
            {
                transaction.ExecutionGate.ValidateStep(step);
                if (family != "reader-string" && !ReferenceEquals(command, value))
                    throw new InvalidOperationException("The borrowed command was replaced.");
                calls++;
            };
            ExecuteSyncOwnedTestFamily(transaction.DatabaseAccess, family, command, step);
            await Assert.That(calls).IsEqualTo(1);
            await Assert.That(disposals).IsEqualTo(0);
        }
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncOwnedCommands_PreserveNullAndTypedScalarConversionPolicy(bool privateDispatch)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ScriptedDbCommand();
        using var read = DataSourceAccess.BeginRead(transaction, "scalar conversion");
        if (privateDispatch)
            fixture.Scenario.SyncOwnedDispatch = (_, _) => { };
        fixture.Scenario.ScalarResult = null;
        await Assert.That(SyncCommandDispatch.ExecuteScalar(transaction.DatabaseAccess, command, read!.Step)).IsNull();
        fixture.Scenario.ScalarResult = DBNull.Value;
        await Assert.That(SyncCommandDispatch.ExecuteScalar(transaction.DatabaseAccess, command, read.Step)).IsSameReferenceAs(DBNull.Value);
        _ = Capture<InvalidCastException>(() => SyncCommandDispatch.ExecuteScalar<int>(transaction.DatabaseAccess, command, read.Step));
        fixture.Scenario.ScalarResult = "42";
        await Assert.That(SyncCommandDispatch.ExecuteScalar<int>(transaction.DatabaseAccess, command, read.Step)).IsEqualTo(42);
        transaction.ExecutionGate.ValidateStep(read.Step);
    }

    [Test]
    [Arguments("update")]
    [Arguments("insert")]
    [Arguments("delete")]
    public async Task SyncOwnedCommands_MutationStatementAndHydrationUseDistinctSteps(string mutation)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var owners = new List<TransactionOperationGate.Step>();
        fixture.Scenario.SyncPublicDispatch = _ => transaction.EnsureCanRead("raw test command");
        fixture.Scenario.SyncOwnedDispatch = (_, step) =>
        {
            transaction.ExecutionGate.ValidateStep(step);
            if (owners.Count > 0)
                _ = Capture<InvalidOperationException>(() => transaction.ExecutionGate.ValidateStep(owners[0]));
            owners.Add(step);
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteNonQuery("reentrant raw mutation"));
            _ = Capture<InvalidOperationException>(transaction.Commit);
        };
        fixture.Scenario.ScalarResult = 401;
        var table = mutation == "insert" ? fixture.AutoTable : fixture.RowTable;
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(table, 401, "stored"));
        IImmutableInstance? saved = null;
        if (mutation == "insert")
            saved = transaction.Insert(fixture.CreateNewAutoMutable("stored"));
        else
        {
            var mutable = fixture.CreateExistingMutable(401, "before");
            if (mutation == "delete") transaction.Delete(mutable);
            else
            {
                mutable["Value"] = "stored";
                saved = transaction.Update(mutable);
            }
        }
        var expectedSteps = mutation == "delete" ? 1 : 2;
        await Assert.That(owners.Count).IsEqualTo(expectedSteps);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(expectedSteps);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        if (saved is not null) await Assert.That(saved.GetReadSource()).IsSameReferenceAs(transaction);
        transaction.Rollback();
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(1);
    }

    [Test]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("reader-cleanup")]
    [Arguments("command-cleanup")]
    [Arguments("mutation")]
    public async Task SyncOwnedCommands_FailureSettlesResourcesBeforeReleasingAdmission(string stage)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new InjectedMutationException(stage);
        var cleanupChecks = 0;
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        fixture.Scenario.SyncPublicDispatch = _ => transaction.EnsureCanRead("raw test command");
        fixture.Scenario.SyncOwnedDispatch = (_, _) => { };
        fixture.Scenario.CommandDisposed = () =>
        {
            cleanupChecks++;
            _ = Capture<InvalidOperationException>(() => transaction.DatabaseAccess.ExecuteScalar("raw during cleanup"));
        };
        fixture.Scenario.ReaderFactory = () => stage == "reader" ? throw expected : reader;
        if (stage == "scalar") fixture.Scenario.ScalarExecuting = () => throw expected;
        if (stage == "reader-cleanup") reader.DisposeFailure = expected;
        if (stage == "command-cleanup") fixture.Scenario.CommandDisposeFailure = expected;
        if (stage == "mutation") fixture.Scenario.EnqueueNonQueryFailure(expected);
        var failure = Capture<InjectedMutationException>(() =>
        {
            if (stage == "mutation") transaction.Delete(fixture.CreateExistingMutable(401, "before"));
            else if (stage == "scalar") transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalar();
            else transaction.From<TransactionMutationGuardRow>().SelectQuery().ReadFirstRow();
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(cleanupChecks).IsEqualTo(1);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
        if (stage is "reader-cleanup" or "command-cleanup") await Assert.That(reader.Disposals).IsEqualTo(1);
        using (transaction.ExecutionGate.Enter("verify settled ownership")) { }
        if (stage == "mutation") await Assert.That(transaction.IsPoisoned).IsTrue();
        transaction.Rollback();
    }

    private static void ExecuteSyncOwnedTestFamily(IDatabaseAccess access, string family,
        IDbCommand command, TransactionOperationGate.Step step)
    {
        switch (family)
        {
            case "reader": using (SyncCommandDispatch.ExecuteReader(access, command, step)) { } break;
            case "reader-string": using (SyncCommandDispatch.ExecuteReader(access, "SELECT rows", step)) { } break;
            case "scalar": _ = SyncCommandDispatch.ExecuteScalar(access, command, step); break;
            case "typed-scalar": _ = SyncCommandDispatch.ExecuteScalar<int>(access, command, step); break;
            case "non-query": _ = SyncCommandDispatch.ExecuteNonQuery(access, command, step); break;
            default: throw new ArgumentOutOfRangeException(nameof(family));
        }
    }

    [Test]
    [Arguments("commit")]
    [Arguments("rollback")]
    [Arguments("dispose")]
    public async Task SyncOwnedCommands_TerminalTransactionRejectsEvenACurrentStep(string completion)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ScriptedDbCommand();
        var dispatched = 0;
        fixture.Scenario.SyncOwnedDispatch = (_, _) => dispatched++;
        if (completion == "commit") transaction.Commit();
        else if (completion == "rollback") transaction.Rollback();
        else transaction.Dispose();
        // A gate token alone cannot revive a terminal managed transaction.
        using var lease = transaction.ExecutionGate.Enter("terminal proof");
        using var step = transaction.ExecutionGate.EnterStep(lease);
        foreach (var family in new[] { "reader", "reader-string", "scalar", "typed-scalar", "non-query" })
        {
            if (completion == "dispose")
                _ = Capture<ObjectDisposedException>(() => ExecuteSyncOwnedTestFamily(transaction.DatabaseAccess, family, command, step));
            else
                _ = Capture<InvalidOperationException>(() => ExecuteSyncOwnedTestFamily(transaction.DatabaseAccess, family, command, step));
        }
        await Assert.That(dispatched).IsEqualTo(0);
    }

    [Test]
    public async Task SyncOwnedCommands_InvalidArgumentsDoNotReachPrivateProviderHooks()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ScriptedDbCommand();
        using var read = DataSourceAccess.BeginRead(transaction, "validation proof");
        var access = (DatabaseAccess)transaction.DatabaseAccess;
        var dispatched = 0;
        fixture.Scenario.SyncOwnedDispatch = (_, _) => dispatched++;
        _ = Capture<ArgumentNullException>(() => access.ExecuteReaderOwned((string)null!, read!.Step));
        _ = Capture<ArgumentNullException>(() => access.ExecuteReaderOwned((IDbCommand)null!, read!.Step));
        _ = Capture<ArgumentNullException>(() => access.ExecuteScalarOwned(null!, read!.Step));
        _ = Capture<ArgumentNullException>(() => access.ExecuteScalarOwned<int>(null!, read!.Step));
        _ = Capture<ArgumentNullException>(() => access.ExecuteNonQueryOwned(null!, read!.Step));
        _ = Capture<ArgumentNullException>(() => access.ExecuteReaderOwned(command, null!));
        _ = Capture<ArgumentNullException>(() => access.ExecuteReaderOwned("SELECT rows", null!));
        _ = Capture<ArgumentNullException>(() => access.ExecuteScalarOwned(command, null!));
        _ = Capture<ArgumentNullException>(() => access.ExecuteScalarOwned<int>(command, null!));
        _ = Capture<ArgumentNullException>(() => access.ExecuteNonQueryOwned(command, null!));
        await Assert.That(dispatched).IsEqualTo(0);
        transaction.ExecutionGate.ValidateStep(read!.Step);
    }

    private sealed partial class ScriptedDatabaseTransaction
    {
        private IDataLinqDataReader ExecuteReaderNative()
        {
            scenario.ReaderExecutions++;
            return scenario.ReaderFactory?.Invoke() ?? EmptyReader.Instance;
        }

        private object? ExecuteScalarNative()
        {
            scenario.ScalarExecutions++;
            scenario.ScalarExecuting?.Invoke();
            return scenario.ScalarResult;
        }

        // Opt-in gated-adapter simulation. Default cases still exercise the base
        // compatibility hooks and existing public virtual methods; private cases
        // dispatch directly, never through a flag granting ambient reentry.
        internal override IDataLinqDataReader ExecuteReaderOwnedCore(IDbCommand command, TransactionOperationGate.Step owner)
        {
            if (scenario.SyncCommands is not null) return BindOwnedSyncTestCommand(command, SyncCommandKind.Reader).ExecuteReader(owner);
            if (scenario.SyncOwnedDispatch is null) return base.ExecuteReaderOwnedCore(command, owner);
            scenario.SyncOwnedDispatch(command, owner);
            return ExecuteReaderNative();
        }

        internal override IDataLinqDataReader ExecuteReaderOwnedCore(string query, TransactionOperationGate.Step owner)
        {
            if (scenario.SyncCommands is not null) return ExecuteOwnedSyncTestReader(query, owner);
            if (scenario.SyncOwnedDispatch is null) return base.ExecuteReaderOwnedCore(query, owner);
            scenario.SyncOwnedDispatch(query, owner);
            return ExecuteReaderNative();
        }

        internal override object? ExecuteScalarOwnedCore(IDbCommand command, TransactionOperationGate.Step owner)
        {
            if (scenario.SyncCommands is not null) return BindOwnedSyncTestCommand(command, SyncCommandKind.Scalar).ExecuteScalar(owner);
            if (scenario.SyncOwnedDispatch is null) return base.ExecuteScalarOwnedCore(command, owner);
            scenario.SyncOwnedDispatch(command, owner);
            return ExecuteScalarNative();
        }

        internal override T ExecuteScalarOwnedCore<T>(IDbCommand command, TransactionOperationGate.Step owner)
        {
            if (scenario.SyncCommands is not null)
            {
                var bound = scenario.SyncCommands.BindScalar<T>(command);
                bound.Command.Reserve(SyncCommandKind.Scalar, hasOwner: true);
                return bound.Convert(bound.Command.ExecuteScalar(owner));
            }
            if (scenario.SyncOwnedDispatch is null) return base.ExecuteScalarOwnedCore<T>(command, owner);
            scenario.SyncOwnedDispatch(command, owner);
            return (T)Convert.ChangeType(ExecuteScalarNative()!, typeof(T));
        }

        internal override int ExecuteNonQueryOwnedCore(IDbCommand command, TransactionOperationGate.Step owner)
        {
            if (scenario.SyncCommands is not null) return BindOwnedSyncTestCommand(command, SyncCommandKind.NonQuery).ExecuteNonQuery(owner);
            if (scenario.SyncOwnedDispatch is null) return base.ExecuteNonQueryOwnedCore(command, owner);
            scenario.SyncOwnedDispatch(command, owner);
            return scenario.ExecuteNonQuery();
        }
    }
}
