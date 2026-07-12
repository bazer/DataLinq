using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Cache;

public partial class TableCache
{
    public bool IsTransactionInCache(Transaction transaction) => transactionRows?.ContainsKey(transaction) == true;

    public IEnumerable<IImmutableInstance> GetTransactionRows(Transaction transaction)
    {
        if (transactionRows?.TryGetValue(transaction, out var result) == true)
            return result.Rows;

        return [];
    }

    public int ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
    {
        var relevantChanges = changes.Where(x => x.Table == Table).ToList();
        if (relevantChanges.Count == 0)
            return 0;

        return transaction == null
            ? ApplyCommittedChanges(relevantChanges)
            : ApplyTransactionChanges(relevantChanges, transaction);
    }

    private int ApplyTransactionChanges(IReadOnlyList<StateChange> changes, Transaction transaction)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var impactBuilder = new CacheInvalidationImpactBuilder();
        var numRows = 0;

        foreach (var change in changes)
        {
            AddStateChangeImpact(change, impactBuilder);

            if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
            {
                if (TryRemoveTransactionRow(change.PrimaryKeys, transaction, out var transRows))
                    numRows += transRows;
            }
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        if (numRows > 0)
        {
            MetricsHandle.RecordCacheCleanup(numRows, duration);
            DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.TransactionStateChange, numRows, duration);
        }

        MetricsHandle.RecordCacheCleanup(0, duration);
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.TransactionStateChangeTable, 0, duration);

        RefreshOccupancyMetrics();
        OnRowChanged(impactBuilder.Build(), transaction);

        return numRows;
    }

    private int ApplyCommittedChanges(IReadOnlyList<StateChange> changes)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var impactBuilder = new CacheInvalidationImpactBuilder();
        var numRows = 0;

        foreach (var change in changes)
        {
            AddStateChangeImpact(change, impactBuilder);

            if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
            {
                if (rowCache is not null && TryRemoveRowFromCache(rowCache, change.PrimaryKeys, out var rows))
                    numRows += rows;
            }

            TryRemoveRowFromAllIndices(change.PrimaryKeys, out var indexRows);
            numRows += indexRows;

            if (change.Type == TransactionChangeType.Update)
            {
                var invalidatedIndices = new HashSet<ColumnIndex>();
                foreach (var changedValue in change.GetChanges())
                {
                    var columnIndices = change.Table.GetColumnIndices(changedValue.Key);
                    for (var i = 0; i < columnIndices.Count; i++)
                    {
                        var columnIndex = columnIndices[i];
                        if (invalidatedIndices.Add(columnIndex))
                            RemoveIndexOnBothSides(
                                columnIndex,
                                change.GetCurrentRelationKey(columnIndex));
                    }
                }
            }
            else
            {
                foreach (var columnIndex in change.Table.ColumnIndices)
                {
                    RemoveIndexOnBothSides(
                        columnIndex,
                        change.GetCurrentRelationKey(columnIndex));
                }
            }
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        if (numRows > 0)
        {
            MetricsHandle.RecordCacheCleanup(numRows, duration);
            DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.StateChangePrecise, numRows, duration);
        }

        MetricsHandle.RecordCacheCleanup(0, duration);
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.StateChangeTable, 0, duration);

        RefreshOccupancyMetrics();
        OnRowChanged(impactBuilder.Build());

        return numRows;

        int RemoveIndexOnBothSides(ColumnIndex columnIndex, DataLinqKey foreignKey)
        {
            if (TryRemoveForeignKeyIndex(columnIndex, foreignKey, out var indexRowsThisSide))
                numRows += indexRowsThisSide;

            foreach (var index in columnIndex.RelationParts.Select(x => x.GetOtherSide().ColumnIndex))
                if (DatabaseCache.GetTableCache(index.Table).TryRemoveForeignKeyIndex(index, foreignKey, out var indexRowsOtherSide))
                    numRows += indexRowsOtherSide;

            return numRows;
        }
    }

    private static void AddStateChangeImpact(StateChange change, CacheInvalidationImpactBuilder impactBuilder)
    {
        if (change.PrimaryKeys.IsNull)
            impactBuilder.ClearTable();
        else
            impactBuilder.AddPrimaryKey(change.PrimaryKeys);

        switch (change.Type)
        {
            case TransactionChangeType.Insert:
            case TransactionChangeType.Delete:
                AddCurrentRelationKeys(change, change.Table.ColumnIndices, impactBuilder);
                break;
            case TransactionChangeType.Update:
                AddUpdatedRelationKeys(change, impactBuilder);
                break;
        }
    }

    private static void AddCurrentRelationKeys(
        StateChange change,
        IEnumerable<ColumnIndex> indices,
        CacheInvalidationImpactBuilder impactBuilder)
    {
        foreach (var index in indices)
            impactBuilder.AddRelationKey(index, change.GetCurrentRelationKey(index));
    }

    private static void AddUpdatedRelationKeys(StateChange change, CacheInvalidationImpactBuilder impactBuilder)
    {
        var invalidatedIndices = new HashSet<ColumnIndex>();
        foreach (var changedValue in change.GetChanges())
        {
            var columnIndices = change.Table.GetColumnIndices(changedValue.Key);
            for (var i = 0; i < columnIndices.Count; i++)
            {
                var columnIndex = columnIndices[i];
                if (!invalidatedIndices.Add(columnIndex))
                    continue;

                impactBuilder.AddRelationKey(
                    columnIndex,
                    change.GetCurrentRelationKey(columnIndex));

                if (TryGetOriginalKey(change, columnIndex.Columns, out var originalKey))
                    impactBuilder.AddRelationKey(columnIndex, originalKey);
                else
                    impactBuilder.ClearTable();
            }
        }
    }

    private static bool TryGetOriginalKey(
        StateChange change,
        IReadOnlyList<ColumnDefinition> columns,
        out DataLinqKey key)
    {
        var values = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            if (!change.TryGetOriginalValue(columns[i], out var value))
            {
                key = DataLinqKey.Null;
                return false;
            }

            values[i] = value;
        }

        key = KeyFactory.CreateKeyFromModelValues(values, columns);
        return true;
    }

    internal bool TryRemoveTransactionRow(DataLinqKey primaryKeys, Transaction transaction, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        return transactionRows?.TryGetValue(transaction, out var transactionRowCache) == true &&
            TryRemoveRowFromCache(transactionRowCache, primaryKeys, out numRowsRemoved);
    }

    internal bool TryRemoveProviderKey<TKey>(TKey primaryKey, IDataSourceAccess dataSource, out int numRowsRemoved)
        where TKey : notnull
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (dataSource is Transaction transaction && transaction.Type != TransactionType.ReadOnly)
        {
            numRowsRemoved = 0;
            return transactionRows?.TryGetValue(transaction, out var transactionRowCache) == true &&
                TryRemoveProviderKeyFromCache(transactionRowCache, primaryKey, out numRowsRemoved);
        }

        return rowCache is not null
            ? TryRemoveProviderKeyFromCache(rowCache, primaryKey, out numRowsRemoved)
            : RemoveNoRows(out numRowsRemoved);
    }

    internal int InvalidateProviderKey<TKey>(TKey providerPrimaryKey, DataLinqKey normalizedPrimaryKey)
        where TKey : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();
        var numRows = 0;

        if (rowCache is not null)
        {
            if (TryRemoveProviderKeyFromCache(rowCache, providerPrimaryKey, out var providerRows))
                numRows += providerRows;

            if (providerRows == 0 && providerPrimaryKey is not DataLinqKey)
            {
                if (TryRemoveRowFromCache(rowCache, normalizedPrimaryKey, out var dynamicRows))
                    numRows += dynamicRows;
            }
        }

        TryRemoveRowFromAllIndices(normalizedPrimaryKey, out var indexRows);
        numRows += indexRows;

        RecordManualInvalidation(numRows, Stopwatch.GetElapsedTime(startedAt), notifySubscribers: true);

        return numRows;
    }

    internal int InvalidateProviderKeys(IReadOnlyList<DataLinqKey> normalizedPrimaryKeys)
    {
        if (normalizedPrimaryKeys.Count == 0)
            return 0;

        var startedAt = Stopwatch.GetTimestamp();
        var numRows = 0;

        for (var i = 0; i < normalizedPrimaryKeys.Count; i++)
        {
            var primaryKey = normalizedPrimaryKeys[i];
            if (rowCache is not null && TryRemoveRowFromCache(rowCache, primaryKey, out var rows))
                numRows += rows;

            TryRemoveRowFromAllIndices(primaryKey, out var indexRows);
            numRows += indexRows;
        }

        RecordManualInvalidation(numRows, Stopwatch.GetElapsedTime(startedAt), notifySubscribers: true);

        return numRows;
    }

    internal int InvalidateProviderKeys(
        IReadOnlyList<DataLinqKey> normalizedPrimaryKeys,
        CacheInvalidationImpact impact)
    {
        if (normalizedPrimaryKeys.Count == 0)
            return 0;

        var startedAt = Stopwatch.GetTimestamp();
        var numRows = 0;

        for (var i = 0; i < normalizedPrimaryKeys.Count; i++)
        {
            var primaryKey = normalizedPrimaryKeys[i];
            if (rowCache is not null && TryRemoveRowFromCache(rowCache, primaryKey, out var rows))
                numRows += rows;

            TryRemoveRowFromAllIndices(primaryKey, out var indexRows);
            numRows += indexRows;
        }

        foreach (var relationKey in impact.ChangedRelationKeys)
        {
            if (!ReferenceEquals(relationKey.Index.Table, Table))
                continue;

            if (TryRemoveForeignKeyIndex(relationKey.Index, relationKey.ProviderKey, out var indexRows))
                numRows += indexRows;
        }

        RecordManualInvalidation(numRows, Stopwatch.GetElapsedTime(startedAt), impact);

        return numRows;
    }

    private void RecordManualInvalidation(int rowsRemoved, TimeSpan duration, bool notifySubscribers = false)
    {
        if (rowsRemoved == 0 && !notifySubscribers)
            return;

        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.ManualInvalidate, rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);

        if (notifySubscribers)
            OnRowChanged();
    }

    private void RecordManualInvalidation(int rowsRemoved, TimeSpan duration, CacheInvalidationImpact impact)
    {
        if (rowsRemoved == 0 && impact.IsEmpty)
            return;

        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.ManualInvalidate, rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        OnRowChanged(impact);
    }

    public bool TryRemoveTransaction(Transaction transaction)
    {
        var rowsByTransaction = transactionRows;
        if (rowsByTransaction?.ContainsKey(transaction) == true)
        {
            var startedAt = Stopwatch.GetTimestamp();
            if (rowsByTransaction.TryRemove(transaction, out var rows))
            {
                var rowsRemoved = rows.Count;
                var duration = Stopwatch.GetElapsedTime(startedAt);
                RefreshOccupancyMetrics();
                DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.TransactionRemove, rowsRemoved, duration);
                MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
                return true;
            }

            return false;
        }

        return true;
    }
}
