using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Instances;

namespace DataLinq.Cache;

public enum CacheInvalidationScope
{
    Database,
    Table,
    Row,
    Rows
}

public static class CacheInvalidationSources
{
    public const string Manual = "manual";
    public const string External = "external";
    public const string Mutation = "mutation";
    public const string Cleanup = "cleanup";
    public const string Freshness = "freshness";
    public const string MemoryPressure = "memory_pressure";
}

public sealed class CacheIndexInvalidation
{
    public IReadOnlyList<string> Columns { get; init; } = [];
    public DataLinqKeyComponents? OldValue { get; init; }
    public DataLinqKeyComponents? NewValue { get; init; }

    public static CacheIndexInvalidation OldAndNew(
        string column,
        DataLinqKeyComponents oldValue,
        DataLinqKeyComponents newValue) =>
        OldAndNew([column], oldValue, newValue);

    public static CacheIndexInvalidation OldAndNew(
        IReadOnlyList<string> columns,
        DataLinqKeyComponents oldValue,
        DataLinqKeyComponents newValue) =>
        new()
        {
            Columns = NormalizeColumns(columns),
            OldValue = oldValue,
            NewValue = newValue
        };

    public static CacheIndexInvalidation Old(
        string column,
        DataLinqKeyComponents oldValue) =>
        Old([column], oldValue);

    public static CacheIndexInvalidation Old(
        IReadOnlyList<string> columns,
        DataLinqKeyComponents oldValue) =>
        new()
        {
            Columns = NormalizeColumns(columns),
            OldValue = oldValue
        };

    public static CacheIndexInvalidation New(
        string column,
        DataLinqKeyComponents newValue) =>
        New([column], newValue);

    public static CacheIndexInvalidation New(
        IReadOnlyList<string> columns,
        DataLinqKeyComponents newValue) =>
        new()
        {
            Columns = NormalizeColumns(columns),
            NewValue = newValue
        };

    private static IReadOnlyList<string> NormalizeColumns(IReadOnlyList<string> columns)
    {
        if (columns is null)
            throw new ArgumentNullException(nameof(columns));

        if (columns.Count == 0)
            throw new ArgumentException("At least one index column is required.", nameof(columns));

        return columns.ToArray();
    }
}

public sealed class CacheInvalidationEvent
{
    public CacheInvalidationScope Scope { get; init; }
    public string? DatabaseName { get; init; }
    public string? TableName { get; init; }
    public IReadOnlyList<DataLinqKeyComponents> ProviderPrimaryKeys { get; init; } = [];
    public IReadOnlyList<string> ChangedColumns { get; init; } = [];
    public IReadOnlyList<CacheIndexInvalidation> ChangedIndexValues { get; init; } = [];
    public string Source { get; init; } = CacheInvalidationSources.External;
    public string? FreshnessToken { get; init; }
    public string? CorrelationId { get; init; }

    public static CacheInvalidationEvent Database(
        string source = CacheInvalidationSources.External,
        string? databaseName = null,
        string? freshnessToken = null,
        string? correlationId = null) =>
        new()
        {
            Scope = CacheInvalidationScope.Database,
            DatabaseName = databaseName,
            Source = source,
            FreshnessToken = freshnessToken,
            CorrelationId = correlationId
        };

    public static CacheInvalidationEvent Table(
        string tableName,
        string source = CacheInvalidationSources.External,
        string? databaseName = null,
        string? freshnessToken = null,
        string? correlationId = null) =>
        new()
        {
            Scope = CacheInvalidationScope.Table,
            DatabaseName = databaseName,
            TableName = tableName,
            Source = source,
            FreshnessToken = freshnessToken,
            CorrelationId = correlationId
        };

    public static CacheInvalidationEvent Row(
        string tableName,
        DataLinqKeyComponents providerPrimaryKey,
        IEnumerable<string>? changedColumns = null,
        IEnumerable<CacheIndexInvalidation>? changedIndexValues = null,
        string source = CacheInvalidationSources.External,
        string? databaseName = null,
        string? freshnessToken = null,
        string? correlationId = null) =>
        WithScope(Rows(
            tableName,
            [providerPrimaryKey],
            changedColumns,
            changedIndexValues,
            source,
            databaseName,
            freshnessToken,
            correlationId),
            CacheInvalidationScope.Row);

    public static CacheInvalidationEvent Rows(
        string tableName,
        IReadOnlyList<DataLinqKeyComponents> providerPrimaryKeys,
        IEnumerable<string>? changedColumns = null,
        IEnumerable<CacheIndexInvalidation>? changedIndexValues = null,
        string source = CacheInvalidationSources.External,
        string? databaseName = null,
        string? freshnessToken = null,
        string? correlationId = null) =>
        new()
        {
            Scope = CacheInvalidationScope.Rows,
            DatabaseName = databaseName,
            TableName = tableName,
            ProviderPrimaryKeys = providerPrimaryKeys?.ToArray() ?? throw new ArgumentNullException(nameof(providerPrimaryKeys)),
            ChangedColumns = changedColumns?.ToArray() ?? [],
            ChangedIndexValues = changedIndexValues?.ToArray() ?? [],
            Source = source,
            FreshnessToken = freshnessToken,
            CorrelationId = correlationId
        };

    private static CacheInvalidationEvent WithScope(CacheInvalidationEvent source, CacheInvalidationScope scope) =>
        new()
        {
            Scope = scope,
            DatabaseName = source.DatabaseName,
            TableName = source.TableName,
            ProviderPrimaryKeys = source.ProviderPrimaryKeys,
            ChangedColumns = source.ChangedColumns,
            ChangedIndexValues = source.ChangedIndexValues,
            Source = source.Source,
            FreshnessToken = source.FreshnessToken,
            CorrelationId = source.CorrelationId
        };
}

public readonly record struct CacheInvalidationResult(
    int RowsRemoved,
    int TablesCleared,
    bool UsedConservativeFallback);
