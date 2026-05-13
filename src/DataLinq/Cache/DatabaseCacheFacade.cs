using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Cache;

/// <summary>
/// Provides explicit cache clearing and provider-key invalidation operations for a database.
/// </summary>
public sealed class DatabaseCacheFacade<TDatabase>
    where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase>
{
    private readonly Database<TDatabase> database;

    internal DatabaseCacheFacade(Database<TDatabase> database)
    {
        this.database = database;
    }

    /// <summary>
    /// Clears all row and index caches for the database.
    /// </summary>
    public void Clear() => database.Provider.State.Cache.ClearCache();

    /// <summary>
    /// Clears row and index caches for the table that backs <typeparamref name="TModel"/>.
    /// </summary>
    public void ClearTable<TModel>()
        where TModel : IImmutableInstance
    {
        ResolveTableCache<TModel>().ClearCache();
    }

    /// <summary>
    /// Clears row and index caches for the specified table.
    /// </summary>
    public void ClearTable(TableDefinition table)
    {
        ResolveTableCache(table).ClearCache();
    }

    /// <summary>
    /// Invalidates one cached row by provider primary key.
    /// </summary>
    /// <returns><see langword="true"/> when a cached row or index entry was removed; otherwise, <see langword="false"/>.</returns>
    public bool Invalidate<TModel, TKey>(TKey providerPrimaryKey)
        where TModel : IImmutableInstance
        where TKey : notnull
    {
        if (providerPrimaryKey is null)
            throw new ArgumentNullException(nameof(providerPrimaryKey));

        var tableCache = ResolveTableCache<TModel>();
        var normalizedKey = ToDataLinqKey(providerPrimaryKey);
        ValidateProviderPrimaryKey(tableCache.Table, normalizedKey, nameof(providerPrimaryKey));

        return tableCache.InvalidateProviderKey(providerPrimaryKey, normalizedKey) > 0;
    }

    /// <summary>
    /// Invalidates one cached row by dynamic provider primary-key components.
    /// </summary>
    /// <returns><see langword="true"/> when a cached row or index entry was removed; otherwise, <see langword="false"/>.</returns>
    public bool Invalidate<TModel>(DataLinqKeyComponents providerPrimaryKey)
        where TModel : IImmutableInstance
    {
        var tableCache = ResolveTableCache<TModel>();
        var normalizedKey = providerPrimaryKey.ToDataLinqKey();
        ValidateProviderPrimaryKey(tableCache.Table, normalizedKey, nameof(providerPrimaryKey));

        return tableCache.InvalidateProviderKey(normalizedKey, normalizedKey) > 0;
    }

    /// <summary>
    /// Invalidates one cached row by table metadata and dynamic provider primary-key components.
    /// </summary>
    /// <returns><see langword="true"/> when a cached row or index entry was removed; otherwise, <see langword="false"/>.</returns>
    public bool Invalidate(TableDefinition table, DataLinqKeyComponents providerPrimaryKey)
    {
        var tableCache = ResolveTableCache(table);
        var normalizedKey = providerPrimaryKey.ToDataLinqKey();
        ValidateProviderPrimaryKey(tableCache.Table, normalizedKey, nameof(providerPrimaryKey));

        return tableCache.InvalidateProviderKey(normalizedKey, normalizedKey) > 0;
    }

    /// <summary>
    /// Invalidates cached rows by table metadata and dynamic provider primary-key components.
    /// </summary>
    /// <returns>The number of cached rows or index entries removed.</returns>
    public int InvalidateMany(TableDefinition table, IReadOnlyList<DataLinqKeyComponents> providerPrimaryKeys)
    {
        if (providerPrimaryKeys is null)
            throw new ArgumentNullException(nameof(providerPrimaryKeys));

        var tableCache = ResolveTableCache(table);
        var normalizedKeys = new DataLinqKey[providerPrimaryKeys.Count];
        for (var i = 0; i < providerPrimaryKeys.Count; i++)
        {
            normalizedKeys[i] = providerPrimaryKeys[i].ToDataLinqKey();
            ValidateProviderPrimaryKey(tableCache.Table, normalizedKeys[i], $"{nameof(providerPrimaryKeys)}[{i}]");
        }

        return tableCache.InvalidateProviderKeys(normalizedKeys);
    }

    /// <summary>
    /// Invalidates cached rows for the table that backs <typeparamref name="TModel"/>.
    /// </summary>
    /// <returns>The number of cached rows or index entries removed.</returns>
    public int InvalidateMany<TModel>(IReadOnlyList<DataLinqKeyComponents> providerPrimaryKeys)
        where TModel : IImmutableInstance
    {
        if (providerPrimaryKeys is null)
            throw new ArgumentNullException(nameof(providerPrimaryKeys));

        var tableCache = ResolveTableCache<TModel>();
        var normalizedKeys = new DataLinqKey[providerPrimaryKeys.Count];
        for (var i = 0; i < providerPrimaryKeys.Count; i++)
        {
            normalizedKeys[i] = providerPrimaryKeys[i].ToDataLinqKey();
            ValidateProviderPrimaryKey(tableCache.Table, normalizedKeys[i], $"{nameof(providerPrimaryKeys)}[{i}]");
        }

        return tableCache.InvalidateProviderKeys(normalizedKeys);
    }

    /// <summary>
    /// Applies a normalized cache invalidation event.
    /// </summary>
    public CacheInvalidationResult Invalidate(CacheInvalidationEvent invalidationEvent)
    {
        if (invalidationEvent is null)
            throw new ArgumentNullException(nameof(invalidationEvent));

        ValidateDatabaseName(invalidationEvent);

        return invalidationEvent.Scope switch
        {
            CacheInvalidationScope.Database => InvalidateDatabaseEvent(),
            CacheInvalidationScope.Table => InvalidateTableEvent(invalidationEvent),
            CacheInvalidationScope.Row => InvalidateRowsEvent(invalidationEvent, expectedSingleRow: true),
            CacheInvalidationScope.Rows => InvalidateRowsEvent(invalidationEvent, expectedSingleRow: false),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidationEvent), invalidationEvent.Scope, "Unsupported cache invalidation scope.")
        };
    }

    private TableCache ResolveTableCache<TModel>()
        where TModel : IImmutableInstance
    {
        if (!database.Provider.Metadata.TryGetTableModel(typeof(TModel), out var tableModel))
            throw new KeyNotFoundException($"No table model registered for model type '{typeof(TModel).FullName ?? typeof(TModel).Name}'.");

        return ResolveTableCache(tableModel.Table);
    }

    private TableCache ResolveTableCache(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("A table name is required for this cache invalidation scope.", nameof(tableName));

        if (!database.Provider.Metadata.TryGetTableModel(tableName, out var tableModel))
            throw new KeyNotFoundException($"No table model registered for database table '{tableName}'.");

        return ResolveTableCache(tableModel.Table);
    }

    private TableCache ResolveTableCache(TableDefinition table)
    {
        if (table is null)
            throw new ArgumentNullException(nameof(table));

        if (database.Provider.State.Cache.TableCaches.TryGetValue(table, out var tableCache))
            return tableCache;

        throw new ArgumentException(
            $"Table '{table.DbName}' does not belong to database '{database.Provider.Metadata.DbName}'.",
            nameof(table));
    }

    private static DataLinqKey ToDataLinqKey<TKey>(TKey providerPrimaryKey)
        where TKey : notnull
    {
        if (providerPrimaryKey is DataLinqKey dataLinqKey)
            return dataLinqKey;

        if (providerPrimaryKey is IProviderKey providerKey)
            return DataLinqKey.FromProviderKey(providerKey);

        return DataLinqKey.FromValue(providerPrimaryKey);
    }

    private CacheInvalidationResult InvalidateDatabaseEvent()
    {
        var rows = database.Provider.State.Cache.TableCaches.Values.Sum(x => x.RowCount);
        var tables = database.Provider.State.Cache.TableCaches.Count;
        Clear();

        return new CacheInvalidationResult(rows, tables, UsedConservativeFallback: true);
    }

    private CacheInvalidationResult InvalidateTableEvent(CacheInvalidationEvent invalidationEvent)
    {
        var tableCache = ResolveTableCache(GetRequiredTableName(invalidationEvent));
        var rows = tableCache.RowCount;
        tableCache.ClearCache();

        return new CacheInvalidationResult(rows, TablesCleared: 1, UsedConservativeFallback: true);
    }

    private CacheInvalidationResult InvalidateRowsEvent(CacheInvalidationEvent invalidationEvent, bool expectedSingleRow)
    {
        var tableCache = ResolveTableCache(GetRequiredTableName(invalidationEvent));
        var primaryKeys = NormalizePrimaryKeys(tableCache.Table, invalidationEvent.ProviderPrimaryKeys, expectedSingleRow);
        var impact = BuildImpact(tableCache.Table, invalidationEvent, primaryKeys, out var usedConservativeFallback);
        var rowsRemoved = tableCache.InvalidateProviderKeys(primaryKeys, impact);

        return new CacheInvalidationResult(
            rowsRemoved,
            TablesCleared: 0,
            usedConservativeFallback);
    }

    private void ValidateDatabaseName(CacheInvalidationEvent invalidationEvent)
    {
        var databaseName = invalidationEvent.DatabaseName;
        if (databaseName is null)
            return;

        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be empty when supplied.", nameof(invalidationEvent));

        if (string.Equals(databaseName, database.Provider.Metadata.DbName, StringComparison.Ordinal) ||
            string.Equals(databaseName, database.Provider.Metadata.Name, StringComparison.Ordinal) ||
            string.Equals(databaseName, typeof(TDatabase).Name, StringComparison.Ordinal) ||
            string.Equals(databaseName, typeof(TDatabase).FullName, StringComparison.Ordinal))
        {
            return;
        }

        throw new ArgumentException(
            $"Invalidation event targets database '{databaseName}', but this cache belongs to database '{database.Provider.Metadata.DbName}'.",
            nameof(invalidationEvent));
    }

    private static string GetRequiredTableName(CacheInvalidationEvent invalidationEvent)
    {
        if (string.IsNullOrWhiteSpace(invalidationEvent.TableName))
            throw new ArgumentException("A table name is required for this cache invalidation scope.", nameof(invalidationEvent));

        return invalidationEvent.TableName;
    }

    private static DataLinqKey[] NormalizePrimaryKeys(
        TableDefinition table,
        IReadOnlyList<DataLinqKeyComponents> providerPrimaryKeys,
        bool expectedSingleRow)
    {
        if (providerPrimaryKeys is null)
            throw new ArgumentNullException(nameof(providerPrimaryKeys));

        if (expectedSingleRow && providerPrimaryKeys.Count != 1)
            throw new ArgumentException("A row invalidation event must contain exactly one provider primary key.", nameof(providerPrimaryKeys));

        if (!expectedSingleRow && providerPrimaryKeys.Count == 0)
            throw new ArgumentException("A rows invalidation event must contain at least one provider primary key.", nameof(providerPrimaryKeys));

        var normalizedKeys = new DataLinqKey[providerPrimaryKeys.Count];
        for (var i = 0; i < normalizedKeys.Length; i++)
        {
            normalizedKeys[i] = providerPrimaryKeys[i].ToDataLinqKey();
            ValidateProviderPrimaryKey(table, normalizedKeys[i], $"{nameof(CacheInvalidationEvent.ProviderPrimaryKeys)}[{i}]");
        }

        return normalizedKeys;
    }

    private static CacheInvalidationImpact BuildImpact(
        TableDefinition table,
        CacheInvalidationEvent invalidationEvent,
        IReadOnlyList<DataLinqKey> primaryKeys,
        out bool usedConservativeFallback)
    {
        var impactBuilder = new CacheInvalidationImpactBuilder();
        for (var i = 0; i < primaryKeys.Count; i++)
            impactBuilder.AddPrimaryKey(primaryKeys[i]);

        var fullyDescribedIndices = new HashSet<ColumnIndex>();
        for (var i = 0; i < invalidationEvent.ChangedIndexValues.Count; i++)
        {
            var changedIndex = invalidationEvent.ChangedIndexValues[i];
            var index = ResolveColumnIndex(table, changedIndex.Columns, $"{nameof(CacheInvalidationEvent.ChangedIndexValues)}[{i}].{nameof(CacheIndexInvalidation.Columns)}");
            var hasValue = false;
            var hasOldValue = changedIndex.OldValue.HasValue;
            var hasNewValue = changedIndex.NewValue.HasValue;

            if (changedIndex.OldValue is { } oldValue)
            {
                hasValue = true;
                impactBuilder.AddRelationKey(index, ValidateIndexKey(index, oldValue, $"{nameof(CacheInvalidationEvent.ChangedIndexValues)}[{i}].{nameof(CacheIndexInvalidation.OldValue)}"));
            }

            if (changedIndex.NewValue is { } newValue)
            {
                hasValue = true;
                impactBuilder.AddRelationKey(index, ValidateIndexKey(index, newValue, $"{nameof(CacheInvalidationEvent.ChangedIndexValues)}[{i}].{nameof(CacheIndexInvalidation.NewValue)}"));
            }

            if (!hasValue)
                throw new ArgumentException("A changed index invalidation must supply an old value, a new value, or both.", nameof(invalidationEvent));

            if (hasOldValue && hasNewValue)
                fullyDescribedIndices.Add(index);
        }

        usedConservativeFallback = ShouldUseConservativeFallback(table, invalidationEvent, fullyDescribedIndices);
        if (usedConservativeFallback)
            impactBuilder.ClearTable();

        return impactBuilder.Build();
    }

    private static bool ShouldUseConservativeFallback(
        TableDefinition table,
        CacheInvalidationEvent invalidationEvent,
        HashSet<ColumnIndex> describedIndices)
    {
        if (invalidationEvent.ChangedColumns.Count == 0 &&
            invalidationEvent.ChangedIndexValues.Count == 0)
        {
            return true;
        }

        foreach (var columnName in invalidationEvent.ChangedColumns)
        {
            var column = ResolveColumn(table, columnName, nameof(CacheInvalidationEvent.ChangedColumns));
            var indices = table.GetColumnIndices(column);
            for (var i = 0; i < indices.Count; i++)
            {
                if (!describedIndices.Contains(indices[i]))
                    return true;
            }
        }

        return false;
    }

    private static ColumnIndex ResolveColumnIndex(
        TableDefinition table,
        IReadOnlyList<string> columnNames,
        string parameterName)
    {
        if (columnNames is null)
            throw new ArgumentNullException(parameterName);

        if (columnNames.Count == 0)
            throw new ArgumentException("At least one index column is required.", parameterName);

        var columns = new ColumnDefinition[columnNames.Count];
        for (var i = 0; i < columns.Length; i++)
            columns[i] = ResolveColumn(table, columnNames[i], parameterName);

        foreach (var index in table.ColumnIndices)
        {
            if (ColumnsMatch(index.Columns, columns))
                return index;
        }

        throw new ArgumentException(
            $"Table '{table.DbName}' has no index with columns '{string.Join(", ", columnNames)}'.",
            parameterName);
    }

    private static ColumnDefinition ResolveColumn(TableDefinition table, string columnName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            throw new ArgumentException("Column names cannot be empty.", parameterName);

        if (table.TryGetColumnByDbName(columnName, out var column) ||
            table.TryGetColumnByPropertyName(columnName, out column))
        {
            return column;
        }

        throw new ArgumentException(
            $"Table '{table.DbName}' has no column or value property named '{columnName}'.",
            parameterName);
    }

    private static bool ColumnsMatch(IReadOnlyList<ColumnDefinition> left, IReadOnlyList<ColumnDefinition> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static DataLinqKey ValidateIndexKey(ColumnIndex index, DataLinqKeyComponents components, string parameterName)
    {
        var key = components.ToDataLinqKey();
        ValidateProviderKey(index.Table, index.Columns, key, parameterName);
        return key;
    }

    private static void ValidateProviderPrimaryKey(TableDefinition table, DataLinqKey key, string parameterName)
    {
        var shape = table.PrimaryKeyShape;
        if (shape.Arity == 0)
            throw new InvalidOperationException($"Table '{table.DbName}' does not have a primary key.");

        if (key.ValueCount != shape.Arity)
            throw new ArgumentException(
                $"Provider primary key for table '{table.DbName}' has {key.ValueCount} component(s), expected {shape.Arity}.",
                parameterName);

        for (var i = 0; i < shape.Arity; i++)
            ValidateProviderPrimaryKeyComponent(table, shape[i], key.GetValue(i), parameterName);
    }

    private static void ValidateProviderKey(
        TableDefinition table,
        IReadOnlyList<ColumnDefinition> columns,
        DataLinqKey key,
        string parameterName)
    {
        if (key.ValueCount != columns.Count)
            throw new ArgumentException(
                $"Provider key for index on table '{table.DbName}' has {key.ValueCount} component(s), expected {columns.Count}.",
                parameterName);

        for (var i = 0; i < columns.Count; i++)
            ValidateProviderKeyComponent(table, columns[i], key.GetValue(i), parameterName);
    }

    private static void ValidateProviderPrimaryKeyComponent(
        TableDefinition table,
        TableKeyComponentDefinition component,
        object? value,
        string parameterName)
    {
        if (value is null)
            throw new ArgumentException(
                $"Provider primary key component '{component.Column.DbName}' for table '{table.DbName}' cannot be null.",
                parameterName);

        var valueType = value.GetType();
        if (GetStoreKind(valueType) == component.ProviderStoreKind)
            return;

        if (component.ProviderClrType is not null)
        {
            var providerType = Nullable.GetUnderlyingType(component.ProviderClrType) ?? component.ProviderClrType;
            if (providerType.IsAssignableFrom(valueType))
                return;
        }

        throw new ArgumentException(
            $"Provider primary key component '{component.Column.DbName}' for table '{table.DbName}' has type '{valueType.FullName}', expected provider type '{GetExpectedTypeDescription(component)}'.",
            parameterName);
    }

    private static void ValidateProviderKeyComponent(
        TableDefinition table,
        ColumnDefinition column,
        object? value,
        string parameterName)
    {
        if (value is null)
            throw new ArgumentException(
                $"Provider key component '{column.DbName}' for table '{table.DbName}' cannot be null.",
                parameterName);

        var valueType = value.GetType();
        if (GetStoreKind(valueType) == TableKeyShape.GetProviderStoreKind(column))
            return;

        var providerType = Nullable.GetUnderlyingType(column.ValueProperty.CsType.Type ?? typeof(object)) ?? column.ValueProperty.CsType.Type;
        if (providerType is not null && providerType != typeof(object) && providerType.IsAssignableFrom(valueType))
            return;

        throw new ArgumentException(
            $"Provider key component '{column.DbName}' for table '{table.DbName}' has type '{valueType.FullName}', expected provider type '{TableKeyShape.GetProviderCsType(column)}'.",
            parameterName);
    }

    private static TableKeyComponentStoreKind GetStoreKind(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(int))
            return TableKeyComponentStoreKind.Int32;

        if (type == typeof(long))
            return TableKeyComponentStoreKind.Int64;

        if (type == typeof(Guid))
            return TableKeyComponentStoreKind.Guid;

        if (type == typeof(string))
            return TableKeyComponentStoreKind.String;

        return TableKeyComponentStoreKind.Unsupported;
    }

    private static string GetExpectedTypeDescription(TableKeyComponentDefinition component)
    {
        if (component.ProviderClrType is not null)
            return (Nullable.GetUnderlyingType(component.ProviderClrType) ?? component.ProviderClrType).FullName ?? component.ProviderClrType.Name;

        return component.ProviderCsType.ToString();
    }
}
