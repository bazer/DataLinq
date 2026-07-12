using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class MutableLifecycleTests
{
    static MutableLifecycleTests()
    {
        var employeesMetadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(EmployeesDb))
            .ValueOrException();
        EmployeesDb.SetDataLinqGeneratedMetadata(employeesMetadata);
        DatabaseDefinition.TryAddLoadedDatabase(typeof(EmployeesDb), employeesMetadata);
    }

    [Test]
    public async Task GeneratedMutable_InheritsLifecycleAndStartsNewWithoutAnOwner()
    {
        var mutable = CreateNewEmployee();
        var initialHash = mutable.GetHashCode();
        var lifecycle = mutable.Lifecycle;

        mutable.first_name = "Changed";

        await Assert.That(mutable is Mutable<Employee>).IsTrue();
        await Assert.That(mutable.IsNew()).IsTrue();
        await Assert.That(lifecycle.RowKind).IsEqualTo(MutableRowKind.New);
        await Assert.That(lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.NoneForNew);
        await Assert.That(lifecycle.ProviderOwner).IsNull();
        await Assert.That(lifecycle.TransactionOwner).IsNull();
        await Assert.That(lifecycle.NeutralReadSourceOwner).IsNull();
        await Assert.That(lifecycle.InvalidationReason).IsNull();
        await Assert.That(mutable.GetHashCode()).IsEqualTo(initialHash);
    }

    [Test]
    public async Task NewDeletedMutables_PreserveTransientIdentityAndNewState()
    {
        var first = CreateNewEmployee();
        var second = CreateNewEmployee();
        var firstHash = first.GetHashCode();
        var secondHash = second.GetHashCode();

        first.SetDeleted();
        second.SetDeleted();

        await Assert.That(first.IsNew()).IsTrue();
        await Assert.That(second.IsNew()).IsTrue();
        await Assert.That(first.IsDeleted()).IsTrue();
        await Assert.That(second.IsDeleted()).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(firstHash);
        await Assert.That(second.GetHashCode()).IsEqualTo(secondHash);
        await Assert.That(first.Equals(second)).IsFalse();
        await Assert.That(second.Equals(first)).IsFalse();
    }

    [Test]
    public async Task TransactionOwnership_UsesExactReferencesAndNormalizesOnlyTheOwningCommitToken()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 17);
        var lookalikeOwner = new MutableTransactionOwnership(provider, transactionId: 17);
        var lifecycle = MutableLifecycle.New();

        lifecycle.ValidateHydratedAdvance();
        lifecycle.AdvanceHydrated(owner);

        var transactionLocal = lifecycle.Snapshot;
        await Assert.That(transactionLocal.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(transactionLocal.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(transactionLocal.ProviderOwner).IsSameReferenceAs(provider);
        await Assert.That(transactionLocal.TransactionOwner).IsSameReferenceAs(owner);
        await Assert.That(transactionLocal.TransactionOwner).IsNotSameReferenceAs(lookalikeOwner);

        lookalikeOwner.MarkCommittedAfterPublication();

        await Assert.That(lifecycle.Snapshot.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(lifecycle.Snapshot.TransactionOwner).IsSameReferenceAs(owner);

        owner.MarkCommittedAfterPublication();

        var committed = lifecycle.Snapshot;
        await Assert.That(committed.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(committed.ProviderOwner).IsSameReferenceAs(provider);
        await Assert.That(committed.TransactionOwner).IsNull();
    }

    [Test]
    public async Task ExplicitCommitPromotion_RequiresExactOwnerAndPreservesDeletedState()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 20);
        var lookalikeOwner = new MutableTransactionOwnership(provider, transactionId: 20);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);
        lifecycle.MarkDeleted(owner);

        await Assert.That(lifecycle.TryPromoteCommitted(lookalikeOwner)).IsFalse();
        await Assert.That(lifecycle.Snapshot.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);

        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsTrue();
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsTrue();

        var committed = lifecycle.Snapshot;
        await Assert.That(owner.Outcome).IsEqualTo(MutableTransactionOutcome.Unresolved);
        await Assert.That(committed.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(committed.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(committed.ProviderOwner).IsSameReferenceAs(provider);
        await Assert.That(committed.TransactionOwner).IsNull();
    }

    [Test]
    public async Task CommittedFinalizationFailure_InvalidatesTokenDerivedLifecyclePermanently()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 21);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);

        owner.MarkCommittedStateFinalizationFailed();
        owner.MarkCommittedAfterPublication();

        var failed = lifecycle.Snapshot;
        await Assert.That(owner.Outcome)
            .IsEqualTo(MutableTransactionOutcome.CommittedStateFinalizationFailed);
        await Assert.That(failed.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(failed.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(failed.TransactionOwner).IsNull();
        await Assert.That(failed.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.CommittedStateFinalizationFailed);
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsFalse();
    }

    [Test]
    public async Task RolledBackOutcome_InvalidatesTokenDerivedLifecyclePermanently()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 22);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);

        owner.MarkRolledBack();
        owner.MarkCommittedAfterPublication();
        owner.MarkRollbackOutcomeUnknown();
        owner.MarkOpenTransactionDisposed();

        var rolledBack = lifecycle.Snapshot;
        await Assert.That(owner.Outcome).IsEqualTo(MutableTransactionOutcome.RolledBack);
        await Assert.That(rolledBack.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(rolledBack.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(rolledBack.TransactionOwner).IsNull();
        await Assert.That(rolledBack.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.RolledBack);
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsFalse();
    }

    [Test]
    public async Task RollbackOutcomeUnknown_InvalidatesTokenDerivedLifecyclePermanently()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 23);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);

        owner.MarkRollbackOutcomeUnknown();
        owner.MarkRolledBack();
        owner.MarkCommittedAfterPublication();
        owner.MarkOpenTransactionDisposed();

        var outcomeUnknown = lifecycle.Snapshot;
        await Assert.That(owner.Outcome)
            .IsEqualTo(MutableTransactionOutcome.RollbackOutcomeUnknown);
        await Assert.That(outcomeUnknown.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(outcomeUnknown.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(outcomeUnknown.TransactionOwner).IsNull();
        await Assert.That(outcomeUnknown.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.RollbackOutcomeUnknown);
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsFalse();
    }

    [Test]
    public async Task OpenTransactionDisposedOutcome_InvalidatesTokenDerivedLifecyclePermanently()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 24);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);

        owner.MarkOpenTransactionDisposed();
        owner.MarkRolledBack();
        owner.MarkCommittedAfterPublication();
        owner.MarkRollbackOutcomeUnknown();

        var disposed = lifecycle.Snapshot;
        await Assert.That(owner.Outcome)
            .IsEqualTo(MutableTransactionOutcome.OpenTransactionDisposed);
        await Assert.That(disposed.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(disposed.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(disposed.TransactionOwner).IsNull();
        await Assert.That(disposed.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.OpenTransactionDisposed);
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsFalse();
    }

    [Test]
    public async Task CommitOutcomeUnknown_InvalidatesTokenDerivedLifecyclePermanently()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 25);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);

        owner.MarkCommitOutcomeUnknown();
        owner.MarkCommittedAfterPublication();
        owner.MarkRolledBack();
        owner.MarkOpenTransactionDisposed();

        var outcomeUnknown = lifecycle.Snapshot;
        await Assert.That(owner.Outcome)
            .IsEqualTo(MutableTransactionOutcome.CommitOutcomeUnknown);
        await Assert.That(outcomeUnknown.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(outcomeUnknown.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(outcomeUnknown.TransactionOwner).IsNull();
        await Assert.That(outcomeUnknown.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.CommitOutcomeUnknown);
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsFalse();
    }

    [Test]
    public async Task ExternalCompletionUnknown_InvalidatesTokenDerivedLifecyclePermanently()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 26);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);

        owner.MarkExternalCompletionUnknown();
        owner.MarkCommittedAfterPublication();
        owner.MarkRolledBack();
        owner.MarkCommitOutcomeUnknown();

        var outcomeUnknown = lifecycle.Snapshot;
        await Assert.That(owner.Outcome)
            .IsEqualTo(MutableTransactionOutcome.ExternalCompletionUnknown);
        await Assert.That(outcomeUnknown.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(outcomeUnknown.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(outcomeUnknown.TransactionOwner).IsNull();
        await Assert.That(outcomeUnknown.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.ExternalCompletionUnknown);
        await Assert.That(lifecycle.TryPromoteCommitted(owner)).IsFalse();
    }

    [Test]
    public async Task TransactionOwnership_ConcurrentCommitSnapshotsNeverTear()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 19);
        var lifecycle = MutableLifecycle.New();
        lifecycle.AdvanceHydrated(owner);
        using var start = new ManualResetEventSlim(false);
        var invalidSnapshots = 0;
        var readers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (var iteration = 0; iteration < 10_000; iteration++)
                {
                    var snapshot = lifecycle.Snapshot;
                    var isValidTransactionLocal =
                        snapshot.BaselineKind == MutableBaselineKind.TransactionLocal &&
                        ReferenceEquals(snapshot.TransactionOwner, owner);
                    var isValidCommitted =
                        snapshot.BaselineKind == MutableBaselineKind.Committed &&
                        snapshot.TransactionOwner is null;

                    if (!isValidTransactionLocal && !isValidCommitted)
                        Interlocked.Increment(ref invalidSnapshots);
                }
            }))
            .ToArray();
        var commit = Task.Run(() =>
        {
            start.Wait();
            owner.MarkCommittedAfterPublication();
        });

        start.Set();
        await Task.WhenAll(readers.Append(commit));

        await Assert.That(invalidSnapshots).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidAndDeletedLifecycle_ResetValidationCannotResurrectTheBaseline()
    {
        using var provider = new IdentityProvider("provider-a");
        var owner = new MutableTransactionOwnership(provider, transactionId: 18);

        var invalid = MutableLifecycle.New();
        invalid.Invalidate(MutableInvalidationReason.RolledBack);

        var invalidAssignmentReset = Capture<InvalidOperationException>(invalid.ValidateAssignmentReset);
        var invalidBaselineReset = Capture<InvalidOperationException>(
            () => invalid.ValidatePublicBaselineReset(default));

        await Assert.That(invalid.Snapshot.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(invalid.Snapshot.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.RolledBack);
        await Assert.That(invalidAssignmentReset.Message).Contains("invalid");
        await Assert.That(invalidBaselineReset.Message).Contains("invalid");

        var deleted = MutableLifecycle.New();
        deleted.AdvanceHydrated(owner);
        deleted.MarkDeleted(owner);

        var deletedAssignmentReset = Capture<InvalidOperationException>(deleted.ValidateAssignmentReset);
        var deletedBaselineReset = Capture<InvalidOperationException>(
            () => deleted.ValidatePublicBaselineReset(default));

        await Assert.That(deleted.Snapshot.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(deleted.Snapshot.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(deleted.Snapshot.ProviderOwner).IsSameReferenceAs(provider);
        await Assert.That(deleted.Snapshot.TransactionOwner).IsSameReferenceAs(owner);
        await Assert.That(deletedAssignmentReset.Message).Contains("deleted");
        await Assert.That(deletedBaselineReset.Message).Contains("deleted");
    }

    [Test]
    public async Task ResetWithReplacement_PrimaryKeyFailureLeavesMutableStateUnchanged()
    {
        var table = CreateAtomicResetTable("atomic_reset_rows");
        var original = CreateAtomicImmutable(table, id: 41, name: "Original");
        var mutable = new Mutable<AtomicImmutable>(original);
        mutable["Name"] = "Pending change";
        var before = CaptureState(mutable);
        var replacement = new AtomicImmutable(
            new AtomicRowData(table, id: 99, name: "Replacement"),
            () => throw new InvalidOperationException("replacement key failure"));

        var exception = Capture<InvalidOperationException>(() => mutable.Reset(replacement));

        await Assert.That(exception.Message).Contains("replacement key failure");
        await AssertStateUnchanged(mutable, before);
    }

    [Test]
    public async Task Reset_PrimaryKeyFailureLeavesMutableStateUnchanged()
    {
        var table = CreateAtomicResetTable("atomic_reset_rows");
        var primaryKeyCalls = 0;
        var original = new AtomicImmutable(
            new AtomicRowData(table, id: 41, name: "Original"),
            () => ++primaryKeyCalls == 1
                ? DataLinqKey.FromValue(41)
                : throw new InvalidOperationException("original key failure"));
        var mutable = new Mutable<AtomicImmutable>(original);
        mutable["Name"] = "Pending change";
        var before = CaptureState(mutable);

        var exception = Capture<InvalidOperationException>(mutable.Reset);

        await Assert.That(exception.Message).Contains("original key failure");
        await Assert.That(primaryKeyCalls).IsEqualTo(2);
        await AssertStateUnchanged(mutable, before);
    }

    [Test]
    public async Task ResetWithReplacement_DifferentTableLeavesMutableStateUnchanged()
    {
        var originalTable = CreateAtomicResetTable("atomic_reset_rows");
        var replacementTable = CreateAtomicResetTable("other_atomic_reset_rows");
        var original = CreateAtomicImmutable(originalTable, id: 41, name: "Original");
        var mutable = new Mutable<AtomicImmutable>(original);
        mutable["Name"] = "Pending change";
        var before = CaptureState(mutable);
        var replacementPrimaryKeyCalls = 0;
        var replacement = new AtomicImmutable(
            new AtomicRowData(replacementTable, id: 99, name: "Replacement"),
            () =>
            {
                replacementPrimaryKeyCalls++;
                return DataLinqKey.FromValue(99);
            });

        var exception = Capture<InvalidOperationException>(() => mutable.Reset(replacement));

        await Assert.That(exception.Message).Contains("different table definition");
        await Assert.That(replacementPrimaryKeyCalls).IsEqualTo(0);
        await AssertStateUnchanged(mutable, before);
    }

    private static MutableEmployee CreateNewEmployee() =>
        new()
        {
            birth_date = new DateOnly(1990, 1, 1),
            first_name = "New",
            last_name = "Employee",
            gender = Employee.Employeegender.F,
            hire_date = new DateOnly(2024, 1, 1)
        };

    private static TableDefinition CreateAtomicResetTable(string tableName)
    {
        var model = new MetadataModelDraft(new CsTypeDeclaration(typeof(AtomicImmutable)))
        {
            ValueProperties =
            [
                new MetadataValuePropertyDraft(
                    "Id",
                    new CsTypeDeclaration(typeof(int)),
                    new MetadataColumnDraft("id") { PrimaryKey = true })
                {
                    CsSize = sizeof(int)
                },
                new MetadataValuePropertyDraft(
                    "Name",
                    new CsTypeDeclaration(typeof(string)),
                    new MetadataColumnDraft("name"))
            ]
        };
        var database = new MetadataDatabaseDraft(
            $"AtomicResetDb_{tableName}",
            new CsTypeDeclaration(typeof(AtomicDatabaseModel)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    model,
                    new MetadataTableDraft(tableName))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static AtomicImmutable CreateAtomicImmutable(
        TableDefinition table,
        int id,
        string name) =>
        new(
            new AtomicRowData(table, id, name),
            () => DataLinqKey.FromValue(id));

    private static AtomicMutableState CaptureState(Mutable<AtomicImmutable> mutable)
    {
        var change = mutable.GetChanges().Single();
        return new AtomicMutableState(
            mutable.GetImmutableInstance()
                ?? throw new InvalidOperationException("Expected an immutable baseline."),
            mutable["Name"],
            change,
            mutable.Lifecycle,
            mutable.PrimaryKeys(),
            mutable.GetHashCode());
    }

    private static async Task AssertStateUnchanged(
        Mutable<AtomicImmutable> mutable,
        AtomicMutableState before)
    {
        var change = mutable.GetChanges().Single();

        await Assert.That(mutable.GetImmutableInstance()).IsSameReferenceAs(before.Immutable);
        await Assert.That(mutable["Name"]).IsEqualTo(before.Name);
        await Assert.That(change.Key).IsSameReferenceAs(before.Change.Key);
        await Assert.That(change.Value).IsEqualTo(before.Change.Value);
        await Assert.That(mutable.Lifecycle).IsEqualTo(before.Lifecycle);
        await Assert.That(mutable.PrimaryKeys()).IsEqualTo(before.PrimaryKey);
        await Assert.That(mutable.GetHashCode()).IsEqualTo(before.HashCode);
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private readonly record struct AtomicMutableState(
        AtomicImmutable Immutable,
        object? Name,
        KeyValuePair<ColumnDefinition, object?> Change,
        MutableLifecycleSnapshot Lifecycle,
        DataLinqKey PrimaryKey,
        int HashCode);

    private sealed class AtomicDatabaseModel;

    private sealed class AtomicImmutable(
        IRowData rowData,
        Func<DataLinqKey> primaryKeyFactory) : IImmutableInstance
    {
        public object? this[string propertyName] =>
            rowData[rowData.Table.Model.ValueProperties[propertyName].Column];

        public object? this[ColumnDefinition column] => rowData[column];

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() =>
            rowData.GetColumnAndValues();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(
            IEnumerable<ColumnDefinition> columns) =>
            rowData.GetColumnAndValues(columns);

        public bool HasPrimaryKeysSet() => !PrimaryKeys().IsNull;
        public ModelDefinition Metadata() => rowData.Table.Model;
        public DataLinqKey PrimaryKeys() => primaryKeyFactory();
        public IRowData GetRowData() => rowData;
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => throw new NotSupportedException();
    }

    private sealed class AtomicRowData : IRowData
    {
        private readonly object?[] values;

        internal AtomicRowData(TableDefinition table, int id, string name)
        {
            Table = table;
            values = new object?[table.ColumnCount];
            values[table.GetColumnByDbName("id").Index] = id;
            values[table.GetColumnByDbName("name").Index] = name;
        }

        public TableDefinition Table { get; }
        public object? this[ColumnDefinition column] => values[column.Index];
        public object? this[int columnIndex] => values[columnIndex];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];

        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column =>
                new KeyValuePair<ColumnDefinition, object?>(column, this[column]));
    }

    private sealed class IdentityProvider(string name) : IDatabaseProvider
    {
        public string TelemetryInstanceId => name;
        public string DatabaseName => name;
        public string ConnectionString => throw new NotSupportedException();
        public DatabaseDefinition Metadata => throw new NotSupportedException();
        public DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public ReadOnlyAccess ReadOnlyAccess => throw new NotSupportedException();
        public DatabaseType DatabaseType => DatabaseType.SQLite;
        public IDbCommand ToDbCommand(IQuery query) => throw new NotSupportedException();
        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) => throw new NotSupportedException();
        public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => throw new NotSupportedException();
        public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public string GetLastIdQuery() => throw new NotSupportedException();
        public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public TableCache GetTableCache(TableDefinition table) => throw new NotSupportedException();
        public string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public bool FileOrServerExists() => throw new NotSupportedException();
        public IDataLinqDataWriter GetWriter() => throw new NotSupportedException();
        public Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public M Commit<M>(Func<Transaction, M> func) => throw new NotSupportedException();
        public void Commit(Action<Transaction> action) => throw new NotSupportedException();
        public bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public IDbConnection GetDbConnection() => throw new NotSupportedException();
        public Sql GetCreateSql() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
