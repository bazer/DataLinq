using System;
using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Cache invalidation metrics.
/// </summary>
/// <param name="Operations">Total number of table-level invalidation records.</param>
/// <param name="RowsRemoved">Total number of rows removed from row or index caches by invalidation.</param>
/// <param name="TablesCleared">Total number of table caches cleared by invalidation.</param>
/// <param name="ProviderKeys">Total number of provider primary keys supplied to invalidation operations.</param>
/// <param name="ChangedColumns">Total number of changed column descriptors supplied to invalidation operations.</param>
/// <param name="ChangedIndexValues">Total number of changed index-value descriptors supplied to invalidation operations.</param>
/// <param name="ApproximateWork">Approximate invalidation work units recorded.</param>
/// <param name="PreciseOperations">Invalidation records that used provider-key precise removal.</param>
/// <param name="ConservativeFallbackOperations">Invalidation records that used a conservative table/database fallback.</param>
/// <param name="DatabaseScopeOperations">Invalidation records from database-scope invalidation.</param>
/// <param name="TableScopeOperations">Invalidation records from table-scope invalidation.</param>
/// <param name="RowScopeOperations">Invalidation records from row-scope invalidation.</param>
/// <param name="RowsScopeOperations">Invalidation records from rows-scope invalidation.</param>
/// <param name="TotalDurationMicroseconds">Cumulative invalidation duration in microseconds.</param>
public readonly record struct CacheInvalidationMetricsSnapshot(
    long Operations,
    long RowsRemoved,
    long TablesCleared,
    long ProviderKeys,
    long ChangedColumns,
    long ChangedIndexValues,
    long ApproximateWork,
    long PreciseOperations,
    long ConservativeFallbackOperations,
    long DatabaseScopeOperations,
    long TableScopeOperations,
    long RowScopeOperations,
    long RowsScopeOperations,
    long TotalDurationMicroseconds)
{
    /// <summary>
    /// Cumulative invalidation duration in milliseconds.
    /// </summary>
    public double TotalDurationMilliseconds => TotalDurationMicroseconds / 1000d;

    internal static CacheInvalidationMetricsSnapshot Sum(IEnumerable<CacheInvalidationMetricsSnapshot> snapshots)
    {
        long operations = 0;
        long rowsRemoved = 0;
        long tablesCleared = 0;
        long providerKeys = 0;
        long changedColumns = 0;
        long changedIndexValues = 0;
        long approximateWork = 0;
        long preciseOperations = 0;
        long conservativeFallbackOperations = 0;
        long databaseScopeOperations = 0;
        long tableScopeOperations = 0;
        long rowScopeOperations = 0;
        long rowsScopeOperations = 0;
        long totalDurationMicroseconds = 0;

        foreach (var snapshot in snapshots)
        {
            operations += snapshot.Operations;
            rowsRemoved += snapshot.RowsRemoved;
            tablesCleared += snapshot.TablesCleared;
            providerKeys += snapshot.ProviderKeys;
            changedColumns += snapshot.ChangedColumns;
            changedIndexValues += snapshot.ChangedIndexValues;
            approximateWork += snapshot.ApproximateWork;
            preciseOperations += snapshot.PreciseOperations;
            conservativeFallbackOperations += snapshot.ConservativeFallbackOperations;
            databaseScopeOperations += snapshot.DatabaseScopeOperations;
            tableScopeOperations += snapshot.TableScopeOperations;
            rowScopeOperations += snapshot.RowScopeOperations;
            rowsScopeOperations += snapshot.RowsScopeOperations;
            totalDurationMicroseconds += snapshot.TotalDurationMicroseconds;
        }

        return new CacheInvalidationMetricsSnapshot(
            Operations: operations,
            RowsRemoved: rowsRemoved,
            TablesCleared: tablesCleared,
            ProviderKeys: providerKeys,
            ChangedColumns: changedColumns,
            ChangedIndexValues: changedIndexValues,
            ApproximateWork: approximateWork,
            PreciseOperations: preciseOperations,
            ConservativeFallbackOperations: conservativeFallbackOperations,
            DatabaseScopeOperations: databaseScopeOperations,
            TableScopeOperations: tableScopeOperations,
            RowScopeOperations: rowScopeOperations,
            RowsScopeOperations: rowsScopeOperations,
            TotalDurationMicroseconds: totalDurationMicroseconds);
    }

    internal static long GetApproximateWork(
        int rowsRemoved,
        int tablesCleared,
        int providerKeyCount,
        int changedColumnCount,
        int changedIndexValueCount) =>
        Math.Max(rowsRemoved, 0) +
        Math.Max(tablesCleared, 0) +
        Math.Max(providerKeyCount, 0) +
        Math.Max(changedColumnCount, 0) +
        Math.Max(changedIndexValueCount, 0);
}
