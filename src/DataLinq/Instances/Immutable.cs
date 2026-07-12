using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public abstract class Immutable<T, M> : IImmutable<T>, IImmutableInstance<M>,
    IEquatable<Immutable<T, M>>, IEquatable<IMutableInstance>, IImmutableBaselineOrigin
    where T : IModel
    where M : class, IDatabaseModel
{
    private readonly IRowData rowData;
    private IDataLinqReadSource readSource;
    private readonly MutableBaselineOrigin baselineOrigin;

    protected Immutable(IRowData rowData, IDataSourceAccess dataSource)
    {
        // Preserve the legacy constructor's historical null behavior. Some row-local projection
        // tests construct immutable shells without a source because they never perform source-bound
        // operations; the new neutral constructor below is the strict contract.
        this.rowData = rowData;
        readSource = dataSource;
        baselineOrigin = MutableBaselineOrigin.FromReadSource(dataSource);
        CaptureCanonicalPrimaryKey(rowData, allowUnavailableLegacyRow: true);
    }

    protected Immutable(IRowData rowData, IDataLinqReadSource readSource)
    {
        ArgumentNullException.ThrowIfNull(rowData);
        ArgumentNullException.ThrowIfNull(readSource);

        this.rowData = rowData;
        this.readSource = readSource;
        baselineOrigin = MutableBaselineOrigin.FromReadSource(readSource);
        CaptureCanonicalPrimaryKey(rowData, allowUnavailableLegacyRow: false);
    }

    MutableBaselineOrigin IImmutableBaselineOrigin.BaselineOrigin => baselineOrigin;

    protected ConcurrentDictionary<RelationProperty, DataLinqKey>? relationKeys;

    protected ConcurrentDictionary<string, object?>? lazyValues = null;

    // Cache the primary key once calculated for performance
    protected DataLinqKey? _cachedPrimaryKey = null;

    private void CaptureCanonicalPrimaryKey(
        IRowData? sourceRow,
        bool allowUnavailableLegacyRow)
    {
        if (sourceRow is null)
            return;

        try
        {
            _cachedPrimaryKey = KeyFactory.GetKey(
                sourceRow,
                sourceRow.Table.PrimaryKeyColumns);
        }
        catch (NotSupportedException) when (allowUnavailableLegacyRow)
        {
            // Legacy row-local projection shells can expose metadata without readable values.
            // Preserve their historical construction behavior; a real key access still fails.
            // The strict neutral-source constructor does not accept this compatibility escape.
        }
    }

    public object? this[ColumnDefinition column] => rowData[column];
    public object? this[int columnIndex] => rowData[columnIndex];
    public object? this[string propertyName] => rowData.GetValue(rowData.Table.Model.ValueProperties[propertyName].Column);

    public ModelDefinition Metadata() => rowData.Table.Model;
    // Use the cached version
    public DataLinqKey PrimaryKeys() => _cachedPrimaryKey ??= KeyFactory.GetKey(rowData, rowData.Table.PrimaryKeyColumns);
    public bool HasPrimaryKeysSet() => !PrimaryKeys().IsNull;

    public IRowData GetRowData() => rowData;
    IRowData IModelInstance.GetRowData() => GetRowData();

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => rowData.GetColumnAndValues();
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => rowData.GetColumnAndValues(columns);


    public void ClearLazy() => lazyValues?.Clear();
    public V? GetLazy<V>(string name, Func<V> fetchCode)
    {
        lazyValues ??= new ConcurrentDictionary<string, object?>();

        if (!lazyValues.TryGetValue(name, out var value) || value == null)
        {
            value = fetchCode();
            lazyValues[name] = value;
        }

        return (V?)value;
    }

    protected ImmutableForeignKey<V> GetImmutableForeignKey<V>(string propertyName) where V : IImmutableInstance
    {
        var property = rowData.Table.Model.RelationProperties[propertyName];
        return GetImmutableForeignKey<V>(property);
    }

    protected ImmutableForeignKey<V> GetImmutableForeignKey<V>(RelationProperty property) where V : IImmutableInstance
    {
        return GetImmutableForeignKeyFromKey<V>(GetRelationKey(property), property);
    }

    protected IImmutableForeignKey<V> GetImmutableForeignKey<V, TKey>(TKey foreignKey, RelationProperty property)
        where V : IImmutableInstance
        where TKey : notnull
    {
        return new ImmutableForeignKey<V, TKey>(foreignKey, GetDataSource(), property);
    }

    protected ImmutableForeignKey<V> GetImmutableForeignKeyFromKey<V>(DataLinqKey foreignKey, RelationProperty property)
        where V : IImmutableInstance
    {
        return new ImmutableForeignKey<V>(foreignKey, GetDataSource(), property);
    }

    protected ImmutableRelation<V> GetImmutableRelation<V>(string propertyName) where V : IImmutableInstance
    {
        var property = rowData.Table.Model.RelationProperties[propertyName];
        return GetImmutableRelation<V>(property);
    }

    protected ImmutableRelation<V> GetImmutableRelation<V>(RelationProperty property) where V : IImmutableInstance
    {
        return GetImmutableRelationFromKey<V>(GetRelationKey(property), property);
    }

    protected ImmutableRelation<V, TKey> GetImmutableRelation<V, TKey>(TKey foreignKey, RelationProperty property)
        where V : IImmutableInstance
        where TKey : notnull
    {
        return new ImmutableRelation<V, TKey>(foreignKey, GetDataSource(), property);
    }

    protected ImmutableRelation<V> GetImmutableRelationFromKey<V>(DataLinqKey foreignKey, RelationProperty property)
        where V : IImmutableInstance
    {
        return new ImmutableRelation<V>(foreignKey, GetDataSource(), property);
    }

    protected object GetValue(string propertyName) => GetNullableValue(propertyName) ?? throw new ArgumentNullException(propertyName);
    protected object? GetNullableValue(string propertyName) => rowData.GetValue(rowData.Table.Model.ValueProperties[propertyName].Column);
    protected object GetValue(int columnIndex) => GetNullableValue(columnIndex) ?? throw new ArgumentNullException(nameof(columnIndex));
    protected object? GetNullableValue(int columnIndex) => rowData.GetValue(columnIndex);
    protected V? GetForeignKey<V>(string propertyName) where V : IImmutableInstance => GetRelation<V>(rowData.Table.Model.RelationProperties[propertyName]).SingleOrDefault();
    protected IEnumerable<V> GetRelation<V>(string propertyName) where V : IImmutableInstance => GetRelation<V>(rowData.Table.Model.RelationProperties[propertyName]);

    protected IEnumerable<V> GetRelation<V>(RelationProperty property) where V : IImmutableInstance
    {
        var source = GetDataSource();

        var otherSide = property.RelationPart.GetOtherSide();
        var result = source.Provider
            .GetTableCache(otherSide.ColumnIndex.Table)
            .GetRows(GetRelationKey(property), property, source)
            .Cast<V>();

        return result;
    }

    private DataLinqKey GetRelationKey(RelationProperty property)
    {
        var keys = LazyInitializer.EnsureInitialized(ref relationKeys);
        return keys.GetOrAdd(property, relationProperty => KeyFactory.GetKey(rowData, relationProperty.RelationPart.ColumnIndex.Columns));
    }

    public IDataSourceAccess GetDataSource()
    {
        var source = GetReadSource();
        if (source is null)
            return null!;

        if (source is IDataSourceAccess dataSource)
            return dataSource;

        throw new InvalidOperationException(
            $"Immutable model '{typeof(T).FullName}' was constructed with a backend-neutral read source " +
            $"('{source.GetType().FullName}') that does not implement {nameof(IDataSourceAccess)}. " +
            $"Use {nameof(GetReadSource)}() for backend-neutral access; legacy provider and relation operations require {nameof(IDataSourceAccess)}.");
    }

    public IDataLinqReadSource GetReadSource()
    {
        if (readSource is Transaction transaction)
        {
            if (transaction.Status == DatabaseTransactionStatus.Committed ||
                transaction.Status == DatabaseTransactionStatus.RolledBack)
            {
                readSource = transaction.Provider.ReadOnlyAccess;
            }
            else
            {
                transaction.EnsureCanRead("access a transaction-bound immutable row");
            }
        }

        return readSource;
    }

    // --- Start of Equality Implementation ---

    public bool Equals(Immutable<T, M>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        // Compare based on primary key
        return this.PrimaryKeys().Equals(other.PrimaryKeys());
    }

    // Implement IEquatable<IMutableInstance>
    public bool Equals(IMutableInstance? other)
    {
        if (other is null) return false;

        // Compare based on primary key
        return this.PrimaryKeys().Equals(other.PrimaryKeys());
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;

        // Check against Immutable first (most common case)
        if (obj is Immutable<T, M> otherImmutable) return Equals(otherImmutable);

        // Check against Mutable
        if (obj is IMutableInstance otherMutable) return Equals(otherMutable);

        return false;
    }

    public override int GetHashCode()
    {
        // Hash code MUST be based *only* on the primary key for consistency and stability
        return PrimaryKeys().GetHashCode();
    }
}
