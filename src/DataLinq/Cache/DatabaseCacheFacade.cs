using System;
using System.Collections.Generic;
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

    private TableCache ResolveTableCache<TModel>()
        where TModel : IImmutableInstance
    {
        if (!database.Provider.Metadata.TryGetTableModel(typeof(TModel), out var tableModel))
            throw new KeyNotFoundException($"No table model registered for model type '{typeof(TModel).FullName ?? typeof(TModel).Name}'.");

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
