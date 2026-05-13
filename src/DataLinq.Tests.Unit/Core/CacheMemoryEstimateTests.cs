using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
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

    private static TableDefinition CreateMemoryEstimateTable()
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

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
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
}
