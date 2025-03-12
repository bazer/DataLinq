using System.Linq;
using DataLinq;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;

public class ImmutableForeignKey<T>(IKey foreignKey, DataSourceAccess dataSource, RelationProperty property) where T : IImmutableInstance
{
    private T? cachedValue;
    private bool isValueCached;

    // Flag to ensure we only attach our listener once.
    private bool _isListenerAttached = false;
    private readonly object _loadLock = new();

    public T? Value => GetInstance();

    private T? GetValue()
    {
        if (foreignKey is NullKey)
            return default;

        var source = GetDataSource();
        var otherSide = property.RelationPart.GetOtherSide();
        var tableCache = source.Provider.GetTableCache(otherSide.ColumnIndex.Table);

        if (!_isListenerAttached)
        {
            _isListenerAttached = true;
            tableCache.RowChanged += OnRowChanged;
        }

        return (T?)tableCache
            .GetRows(foreignKey, property, dataSource)
            .SingleOrDefault();
    }

    // Event handler that clears the cached relation when any change occurs.
    private void OnRowChanged(object? sender, RowChangeEventArgs e) => Clear();

    protected DataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    protected T? GetInstance()
    {
        if (!isValueCached)
        {
            lock (_loadLock)
            {
                cachedValue = GetValue();
                isValueCached = true;
            }
        }

        return cachedValue;
    }

    public void Clear()
    {
        isValueCached = false;
    }

    public static implicit operator T?(ImmutableForeignKey<T> foreignKey) => foreignKey.Value;

    public override string ToString() => Value?.ToString() ?? "null";
}
