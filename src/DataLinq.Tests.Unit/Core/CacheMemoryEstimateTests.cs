using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

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

    private sealed class TestImmutableInstance(DataLinqKey primaryKeys) : IImmutableInstance
    {
        public object? this[string propertyName] => throw new NotSupportedException();
        public object? this[ColumnDefinition column] => throw new NotSupportedException();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => throw new NotSupportedException();
        public DataLinqKey PrimaryKeys() => primaryKeys;
        public IRowData GetRowData() => throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => throw new NotSupportedException();
    }
}
