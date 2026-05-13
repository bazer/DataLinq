using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class CacheMemoryEstimateTests
{
    [Test]
    public async Task CacheMemoryEstimate_Empty_IsZero()
    {
        var estimate = CacheMemoryEstimate.Empty;

        await Assert.That(estimate.RowPayloadBytes).IsEqualTo(0);
        await Assert.That(estimate.RowStoreOverheadBytes).IsEqualTo(0);
        await Assert.That(estimate.TransactionRowPayloadBytes).IsEqualTo(0);
        await Assert.That(estimate.TransactionRowStoreOverheadBytes).IsEqualTo(0);
        await Assert.That(estimate.IndexPayloadBytes).IsEqualTo(0);
        await Assert.That(estimate.IndexOverheadBytes).IsEqualTo(0);
        await Assert.That(estimate.RelationObjectBytes).IsEqualTo(0);
        await Assert.That(estimate.NotificationBytes).IsEqualTo(0);
        await Assert.That(estimate.SnapshotBytes).IsEqualTo(0);
        await Assert.That(estimate.EstimatedCacheBytes).IsEqualTo(0);
    }

    [Test]
    public async Task CacheMemoryEstimate_EstimatedCacheBytes_AddsComponents()
    {
        var estimate = new CacheMemoryEstimate(
            RowPayloadBytes: 1,
            RowStoreOverheadBytes: 2,
            TransactionRowPayloadBytes: 3,
            TransactionRowStoreOverheadBytes: 4,
            IndexPayloadBytes: 5,
            IndexOverheadBytes: 6,
            RelationObjectBytes: 7,
            NotificationBytes: 8,
            SnapshotBytes: 9);

        await Assert.That(estimate.EstimatedCacheBytes).IsEqualTo(45);
    }

    [Test]
    public async Task CacheMemoryEstimate_Addition_AddsMatchingComponents()
    {
        var left = new CacheMemoryEstimate(
            RowPayloadBytes: 1,
            RowStoreOverheadBytes: 2,
            TransactionRowPayloadBytes: 3,
            TransactionRowStoreOverheadBytes: 4,
            IndexPayloadBytes: 5,
            IndexOverheadBytes: 6,
            RelationObjectBytes: 7,
            NotificationBytes: 8,
            SnapshotBytes: 9);
        var right = new CacheMemoryEstimate(
            RowPayloadBytes: 10,
            RowStoreOverheadBytes: 20,
            TransactionRowPayloadBytes: 30,
            TransactionRowStoreOverheadBytes: 40,
            IndexPayloadBytes: 50,
            IndexOverheadBytes: 60,
            RelationObjectBytes: 70,
            NotificationBytes: 80,
            SnapshotBytes: 90);

        var total = left + right;

        await Assert.That(total.RowPayloadBytes).IsEqualTo(11);
        await Assert.That(total.RowStoreOverheadBytes).IsEqualTo(22);
        await Assert.That(total.TransactionRowPayloadBytes).IsEqualTo(33);
        await Assert.That(total.TransactionRowStoreOverheadBytes).IsEqualTo(44);
        await Assert.That(total.IndexPayloadBytes).IsEqualTo(55);
        await Assert.That(total.IndexOverheadBytes).IsEqualTo(66);
        await Assert.That(total.RelationObjectBytes).IsEqualTo(77);
        await Assert.That(total.NotificationBytes).IsEqualTo(88);
        await Assert.That(total.SnapshotBytes).IsEqualTo(99);
        await Assert.That(total.EstimatedCacheBytes).IsEqualTo(495);
    }

    [Test]
    public async Task CacheMemoryEstimate_Sum_SaturatesInsteadOfOverflowing()
    {
        var total = CacheMemoryEstimate.Sum(
        [
            new CacheMemoryEstimate(RowPayloadBytes: long.MaxValue - 10),
            new CacheMemoryEstimate(RowPayloadBytes: 20)
        ]);

        await Assert.That(total.RowPayloadBytes).IsEqualTo(long.MaxValue);
        await Assert.That(total.EstimatedCacheBytes).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task CacheMemoryEstimate_NegativeComponents_DoNotReduceEstimate()
    {
        var estimate = new CacheMemoryEstimate(
            RowPayloadBytes: -100,
            RowStoreOverheadBytes: 25);

        await Assert.That(estimate.EstimatedCacheBytes).IsEqualTo(25);
    }

    [Test]
    public async Task CacheMemoryEstimator_ObjectLayoutHelpers_UsePointerSizedReferences()
    {
        await Assert.That(CacheMemoryEstimator.ReferenceSize).IsEqualTo(IntPtr.Size);
        await Assert.That(CacheMemoryEstimator.ObjectHeaderBytes).IsEqualTo(IntPtr.Size * 2);
        await Assert.That(CacheMemoryEstimator.ObjectArrayBytes(3)).IsEqualTo(
            CacheMemoryEstimator.ArrayHeaderBytes + (IntPtr.Size * 3L));
        await Assert.That(CacheMemoryEstimator.ByteArrayBytes(5)).IsEqualTo(
            CacheMemoryEstimator.ArrayHeaderBytes + 5L);
        await Assert.That(CacheMemoryEstimator.StringBytes("abc")).IsGreaterThan(3L * sizeof(char));
    }

    [Test]
    public async Task CacheMemoryEstimator_DataLinqKeyComponentArrayBytes_CountsOnlyCompositeArrays()
    {
        var scalarKey = DataLinqKey.FromValue(42);
        var compositeKey = DataLinqKey.FromValues([42, "dept-1"]);

        await Assert.That(CacheMemoryEstimator.DataLinqKeyComponentArrayBytes(scalarKey)).IsEqualTo(0);
        await Assert.That(CacheMemoryEstimator.DataLinqKeyComponentArrayBytes(compositeKey)).IsEqualTo(
            CacheMemoryEstimator.ObjectArrayBytes(2));
    }

    [Test]
    public async Task CacheMemoryEstimator_DictionaryOverheadBytes_IsDefensive()
    {
        await Assert.That(CacheMemoryEstimator.DictionaryOverheadBytes(-1)).IsEqualTo(
            CacheMemoryEstimator.DictionaryOverheadBytes(0));
        await Assert.That(CacheMemoryEstimator.DictionaryOverheadBytes(2)).IsGreaterThan(
            CacheMemoryEstimator.DictionaryOverheadBytes(1));
    }

    [Test]
    public async Task CacheByteAliases_StillReportRowPayloadBytes()
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(42));

        await Assert.That(cache.TryAddRow(42, 1536, row)).IsTrue();

        await Assert.That(cache.RowPayloadBytes).IsEqualTo(1536);
        await Assert.That(cache.TotalBytes).IsEqualTo(cache.RowPayloadBytes);
        await Assert.That(cache.TotalBytesFormatted).IsEqualTo(cache.RowPayloadBytesFormatted);

        var tableSnapshot = new TableCacheSnapshot(
            tableName: "employees",
            rowCount: 1,
            totalBytes: 1536,
            newestTick: 10,
            oldestTick: 5,
            indices: []);
        var databaseSnapshot = new DatabaseCacheSnapshot(
            DateTime.UtcNow,
            [
                tableSnapshot,
                new TableCacheSnapshot(
                    tableName: "departments",
                    rowCount: 1,
                    totalBytes: 512,
                    newestTick: 9,
                    oldestTick: 4,
                    indices: [])
            ]);
        var occupancy = new CacheOccupancyMetricsSnapshot(
            Rows: 2,
            TransactionRows: 0,
            Bytes: 2048,
            IndexEntries: 0);

        await Assert.That(tableSnapshot.RowPayloadBytes).IsEqualTo(1536);
        await Assert.That(tableSnapshot.TotalBytes).IsEqualTo(tableSnapshot.RowPayloadBytes);
        await Assert.That(tableSnapshot.TotalBytesFormatted).IsEqualTo(tableSnapshot.RowPayloadBytesFormatted);

        await Assert.That(databaseSnapshot.RowPayloadBytes).IsEqualTo(2048);
        await Assert.That(databaseSnapshot.TotalBytes).IsEqualTo(databaseSnapshot.RowPayloadBytes);
        await Assert.That(databaseSnapshot.TotalBytesFormatted).IsEqualTo(databaseSnapshot.RowPayloadBytesFormatted);

        await Assert.That(occupancy.RowPayloadBytes).IsEqualTo(occupancy.Bytes);
    }

    [Test]
    public async Task RowCache_GetMemoryEstimate_SeparatesRowPayloadFromRowStoreOverhead()
    {
        var table = CreateMemoryEstimateTable();
        using var reader = new FakeDataLinqDataReader([1, "Ada"]);
        var rowData = new RowData(reader, table, table.Columns, hasIndexedColumns: true);
        var cache = new RowCache();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(1), rowData);

        await Assert.That(cache.TryAddRow(1, rowData, row)).IsTrue();

        var estimate = cache.GetMemoryEstimate();

        await Assert.That(estimate.RowPayloadBytes).IsEqualTo(rowData.Size);
        await Assert.That(estimate.RowStoreOverheadBytes).IsGreaterThan(0);
        await Assert.That(estimate.EstimatedCacheBytes).IsGreaterThan(estimate.RowPayloadBytes);
    }

    [Test]
    public async Task RowCache_GetMemoryEstimate_DropsRowPayloadAndOwnedOverheadOnClear()
    {
        var table = CreateMemoryEstimateTable();
        using var reader = new FakeDataLinqDataReader([1, "Ada"]);
        var rowData = new RowData(reader, table, table.Columns, hasIndexedColumns: true);
        var cache = new RowCache();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(1), rowData);

        await Assert.That(cache.TryAddRow(1, rowData, row)).IsTrue();
        var occupiedEstimate = cache.GetMemoryEstimate();

        cache.ClearRows();

        var clearedEstimate = cache.GetMemoryEstimate();

        await Assert.That(clearedEstimate.RowPayloadBytes).IsEqualTo(0);
        await Assert.That(clearedEstimate.RowStoreOverheadBytes).IsLessThan(occupiedEstimate.RowStoreOverheadBytes);
        await Assert.That(clearedEstimate.EstimatedCacheBytes).IsLessThan(occupiedEstimate.EstimatedCacheBytes);
    }

    [Test]
    public async Task IndexCache_GetMemoryEstimate_CountsPrimaryKeyPayloadAndOverhead()
    {
        var cache = new TypedIndexCache<int>();
        var primaryKeys = new[]
        {
            DataLinqKey.FromValues([1, "dept-1"]),
            DataLinqKey.FromValue(2)
        };

        await Assert.That(cache.TryAdd(10, primaryKeys)).IsTrue();

        var estimate = cache.GetMemoryEstimate();

        await Assert.That(estimate.IndexPayloadBytes).IsGreaterThan(0);
        await Assert.That(estimate.IndexOverheadBytes).IsGreaterThan(0);
        await Assert.That(estimate.EstimatedCacheBytes).IsEqualTo(
            estimate.IndexPayloadBytes + estimate.IndexOverheadBytes);
    }

    [Test]
    public async Task IndexCache_GetMemoryEstimate_DropsPayloadAndOverheadOnClear()
    {
        var cache = new TypedIndexCache<string>();

        await Assert.That(cache.TryAdd("dept-1", [DataLinqKey.FromValue(1), DataLinqKey.FromValue(2)])).IsTrue();
        await Assert.That(cache.TryAdd("dept-2", [DataLinqKey.FromValue(3)])).IsTrue();
        var occupiedEstimate = cache.GetMemoryEstimate();

        cache.Clear();

        var clearedEstimate = cache.GetMemoryEstimate();

        await Assert.That(clearedEstimate.IndexPayloadBytes).IsEqualTo(0);
        await Assert.That(clearedEstimate.IndexOverheadBytes).IsLessThan(occupiedEstimate.IndexOverheadBytes);
        await Assert.That(clearedEstimate.EstimatedCacheBytes).IsLessThan(occupiedEstimate.EstimatedCacheBytes);
    }

    [Test]
    public async Task IndexCache_RemovePrimaryKey_RemovesReverseMappingsUnderCacheLock()
    {
        var cache = new TypedIndexCache<int>();
        var primaryKey = DataLinqKey.FromValue(42);

        await Assert.That(cache.TryAdd(1, [primaryKey])).IsTrue();
        await Assert.That(cache.TryAdd(2, [primaryKey])).IsTrue();

        await Assert.That(cache.TryRemove(1, out var removedByForeignKey)).IsTrue();
        await Assert.That(removedByForeignKey).IsEqualTo(1);
        await Assert.That(cache.TryRemovePrimaryKey(primaryKey, out var removedByPrimaryKey)).IsTrue();

        await Assert.That(removedByPrimaryKey).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.GetMemoryEstimate().IndexPayloadBytes).IsEqualTo(0);
    }

    [Test]
    public async Task CacheHistory_GetMemoryEstimate_CountsSnapshotBytesAndDropsOnClear()
    {
        var history = new CacheHistory();
        var emptyEstimate = history.GetMemoryEstimate();

        history.Add(new DatabaseCacheSnapshot(
            DateTime.UtcNow,
            [
                new TableCacheSnapshot(
                    tableName: "employees",
                    rowCount: 1,
                    totalBytes: 128,
                    newestTick: 10,
                    oldestTick: 5,
                    indices: [("idx_employees_department", 2)])
            ]));

        var occupiedEstimate = history.GetMemoryEstimate();

        await Assert.That(occupiedEstimate.SnapshotBytes).IsGreaterThan(emptyEstimate.SnapshotBytes);

        history.Clear();

        await Assert.That(history.GetMemoryEstimate().SnapshotBytes).IsLessThan(occupiedEstimate.SnapshotBytes);
    }

    [Test]
    [NotInParallel]
    public async Task TableCache_GetMemoryEstimate_AggregatesLoadedComponents()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;
        DataLinqMetrics.Reset();

        try
        {
            var metadata = CreateMemoryEstimateDatabase();
            var provider = new FakeDatabaseProvider(metadata);
            using var databaseCache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration);
            provider.Cache = databaseCache;
            var tableCache = databaseCache.TableCaches.Values.Single();
            var table = tableCache.Table;
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);

            SetPrivateField(tableCache, "rowCache", CreatePopulatedRowCache(table, 1, "Ada"));
            SetPrivateField(
                tableCache,
                "transactionRows",
                new ConcurrentDictionary<Transaction, RowCache>(
                    new[] { new KeyValuePair<Transaction, RowCache>(transaction, CreatePopulatedRowCache(table, 2, "Grace")) }));

            var indexCache = new TypedIndexCache<int>();
            await Assert.That(indexCache.TryAdd(1, [DataLinqKey.FromValue(1)])).IsTrue();
            SetPrivateField(
                tableCache,
                "indexCaches",
                new Dictionary<ColumnIndex, IIndexCache>
                {
                    [table.ColumnIndices.Single(x => x.Characteristic == IndexCharacteristic.PrimaryKey)] = indexCache
                });

            var subscriber = new TestCacheNotification();
            tableCache.SubscribeToChanges(subscriber);

            var estimate = tableCache.GetMemoryEstimate();

            await Assert.That(estimate.RowPayloadBytes).IsGreaterThan(0);
            await Assert.That(estimate.RowStoreOverheadBytes).IsGreaterThan(0);
            await Assert.That(estimate.TransactionRowPayloadBytes).IsGreaterThan(0);
            await Assert.That(estimate.TransactionRowStoreOverheadBytes).IsGreaterThan(0);
            await Assert.That(estimate.IndexPayloadBytes).IsGreaterThan(0);
            await Assert.That(estimate.IndexOverheadBytes).IsGreaterThan(0);
            await Assert.That(estimate.NotificationBytes).IsGreaterThan(0);
        }
        finally
        {
            DataLinqMetrics.Reset();
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task DatabaseCache_GetMemoryEstimate_AggregatesProviderCacheAndTransactionRemoval()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;
        DataLinqMetrics.Reset();

        try
        {
            var metadata = CreateMemoryEstimateDatabase();
            var provider = new FakeDatabaseProvider(metadata);
            using var databaseCache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration);
            provider.Cache = databaseCache;
            var tableCache = databaseCache.TableCaches.Values.Single();
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);

            SetPrivateField(
                tableCache,
                "transactionRows",
                new ConcurrentDictionary<Transaction, RowCache>(
                    new[] { new KeyValuePair<Transaction, RowCache>(transaction, CreatePopulatedRowCache(tableCache.Table, 1, "Ada")) }));

            var occupiedEstimate = databaseCache.GetMemoryEstimate();

            await Assert.That(occupiedEstimate.TransactionRowPayloadBytes).IsGreaterThan(0);
            await Assert.That(occupiedEstimate.TransactionRowStoreOverheadBytes).IsGreaterThan(0);

            databaseCache.RemoveTransaction(transaction);

            var removedEstimate = databaseCache.GetMemoryEstimate();

            await Assert.That(removedEstimate.TransactionRowPayloadBytes).IsEqualTo(0);
            await Assert.That(removedEstimate.TransactionRowStoreOverheadBytes).IsLessThan(occupiedEstimate.TransactionRowStoreOverheadBytes);

            _ = databaseCache.MakeSnapshot();

            await Assert.That(databaseCache.GetMemoryEstimate().SnapshotBytes).IsGreaterThan(removedEstimate.SnapshotBytes);
        }
        finally
        {
            DataLinqMetrics.Reset();
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    public async Task RuntimeAggregation_CanSumProviderMemoryEstimates()
    {
        var providerA = new CacheMemoryEstimate(RowPayloadBytes: 10, IndexPayloadBytes: 20);
        var providerB = new CacheMemoryEstimate(RowPayloadBytes: 5, NotificationBytes: 7);

        var runtimeEstimate = CacheMemoryEstimate.Sum([providerA, providerB]);

        await Assert.That(runtimeEstimate.RowPayloadBytes).IsEqualTo(15);
        await Assert.That(runtimeEstimate.IndexPayloadBytes).IsEqualTo(20);
        await Assert.That(runtimeEstimate.NotificationBytes).IsEqualTo(7);
        await Assert.That(runtimeEstimate.EstimatedCacheBytes).IsEqualTo(42);
    }

    private static DatabaseDefinition CreateMemoryEstimateDatabase()
    {
        var draft = new MetadataDatabaseDraft(
            "MemoryEstimateTestDb",
            new CsTypeDeclaration("MemoryEstimateTestDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("MemoryEstimateTestRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                CsSize = sizeof(int)
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "text")]
                                })
                        ]
                    },
                    new MetadataTableDraft("memory_estimate_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static TableDefinition CreateMemoryEstimateTable() =>
        CreateMemoryEstimateDatabase().TableModels.Single().Table;

    private static RowCache CreatePopulatedRowCache(TableDefinition table, int id, string name)
    {
        using var reader = new FakeDataLinqDataReader([id, name]);
        var rowData = new RowData(reader, table, table.Columns, hasIndexedColumns: true);
        var cache = new RowCache();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(id), rowData);
        cache.TryAddRow(id, rowData, row);
        return cache;
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
            throw new MissingFieldException(target.GetType().FullName, fieldName);

        field.SetValue(target, value);
    }

    private sealed class FakeDataLinqDataReader(IReadOnlyList<object?> values) : IDataLinqDataReader
    {
        public object GetValue(int ordinal) => values[ordinal]!;
        public T? GetValue<T>(ColumnDefinition column) => (T?)values[column.Index];
        public T? GetValue<T>(ColumnDefinition column, int ordinal) => (T?)values[ordinal];
        public void Dispose()
        {
        }

        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetString(int ordinal) => throw new NotSupportedException();
        public bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public int GetInt32(int ordinal) => throw new NotSupportedException();
        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
        public Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public bool ReadNextRow() => throw new NotSupportedException();
        public bool IsDbNull(int ordinal) => values[ordinal] is null;
    }

    private sealed class TestImmutableInstance(DataLinqKey primaryKeys, IRowData? rowData = null) : IImmutableInstance
    {
        public object? this[string propertyName] => throw new NotSupportedException();
        public object? this[ColumnDefinition column] => throw new NotSupportedException();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => throw new NotSupportedException();
        public DataLinqKey PrimaryKeys() => primaryKeys;
        public IRowData GetRowData() => rowData ?? throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => throw new NotSupportedException();
    }

    private sealed class TestCacheNotification : ICacheNotification
    {
        public void Clear()
        {
        }
    }

    private sealed class FakeDatabaseProvider(DatabaseDefinition metadata) : IDatabaseProvider
    {
        public DatabaseCache? Cache { get; set; }

        public string TelemetryInstanceId { get; } = Guid.NewGuid().ToString("N");
        public string DatabaseName => metadata.DbName;
        public string ConnectionString => throw new NotSupportedException();
        public DatabaseDefinition Metadata => metadata;
        public DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public ReadOnlyAccess ReadOnlyAccess => new(this);
        public DatabaseType DatabaseType => DatabaseType.SQLite;

        public IDbCommand ToDbCommand(IQuery query) => throw new NotSupportedException();
        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) => new(this, transactionType);
        public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => new FakeDatabaseTransaction(type);
        public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public string GetLastIdQuery() => throw new NotSupportedException();
        public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public TableCache GetTableCache(TableDefinition table) =>
            Cache?.GetTableCache(table) ?? throw new NotSupportedException();
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
        public void Dispose()
        {
        }
    }

    private sealed class FakeDatabaseTransaction(TransactionType type) : DatabaseTransaction(type)
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
        public override void Rollback() => throw new NotSupportedException();
        public override void Commit() => throw new NotSupportedException();
        public override void Dispose()
        {
        }
    }
}
