using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

// Add IEquatable interfaces
public class Mutable<T> : IMutableInstance,
    IEquatable<Mutable<T>>, IEquatable<T> // T is the Immutable type
    where T : class, IImmutableInstance
{
    private readonly ModelDefinition metadata;
    public ModelDefinition Metadata() => metadata;

    protected ConcurrentDictionary<string, object?>? lazyValues = null;
    private T? immutableInstance; // Original immutable state
    public T? GetImmutableInstance() => immutableInstance;

    private bool isNew;
    public bool IsNew() => isNew;
    private bool isDeleted;
    public bool IsDeleted() => isDeleted;
    public bool HasChanges() => mutableRowData.HasChanges();

    private MutableRowData mutableRowData;
    public MutableRowData GetRowData() => mutableRowData;
    IRowData IModelInstance.GetRowData() => GetRowData();

    // Cache the primary key for performance and stability
    private IKey? _cachedPrimaryKey = null;
    private bool _isPkCached = false;

    // Calculate PK on demand, based on current state (new or mutated)
    private IKey GetCurrentPrimaryKey()
    {
        // If already cached and no relevant PK fields changed, return cached.
        // Check if PK fields are among the changes.
        bool pkChanged = false;
        if (HasChanges()) // Only check if there are *any* changes
        {
            var pkColumns = metadata.Table.PrimaryKeyColumns;
            pkChanged = GetChanges().Any(kvp => pkColumns.Contains(kvp.Key));
        }


        if (_isPkCached && _cachedPrimaryKey != null && !pkChanged)
        {
            return _cachedPrimaryKey;
        }

        // Recalculate PK based on current data in mutableRowData
        _cachedPrimaryKey = KeyFactory.CreateKeyFromValues(mutableRowData.GetValues(metadata.Table.PrimaryKeyColumns));
        _isPkCached = true; // Mark as cached
        return _cachedPrimaryKey;
    }

    // Public method to get PK, always uses the cached/recalculated value
    public IKey PrimaryKeys() => GetCurrentPrimaryKey();

    public object? this[ColumnDefinition column]
    {
        get => mutableRowData[column];
        set
        {
            // If a PK column is changed, invalidate the cached PK
            if (metadata.Table.PrimaryKeyColumns.Contains(column))
            {
                _isPkCached = false;
                _cachedPrimaryKey = null;
            }
            mutableRowData.SetValue(column, value);
        }
    }

    public object? this[string propertyName]
    {
        get => mutableRowData.GetValue(metadata.ValueProperties[propertyName].Column);
        set
        {
            var column = metadata.ValueProperties[propertyName].Column;
            // If a PK column is changed, invalidate the cached PK
            if (metadata.Table.PrimaryKeyColumns.Contains(column))
            {
                _isPkCached = false;
                _cachedPrimaryKey = null;
            }
            mutableRowData.SetValue(column, value);
        }
    }

    public Mutable()
    {
        metadata = ModelDefinition.Find<T>() ?? throw new InvalidOperationException($"Model {typeof(T).Name} not found");
        this.mutableRowData = new MutableRowData(metadata.Table);
        isNew = true;
        // Initialize PK cache status
        _isPkCached = false;
        _cachedPrimaryKey = null;
    }

    public Mutable(T model)
    {
        this.immutableInstance = model;
        this.mutableRowData = new MutableRowData(model.GetRowData());
        this.metadata = model.Metadata();
        this.isNew = false;
        // Initialize PK cache from the immutable instance
        _cachedPrimaryKey = model.PrimaryKeys(); // Get PK from immutable source
        _isPkCached = true;
    }

    // Constructor from RowData might be less common for mutable, but included for completeness
    public Mutable(RowData rowData)
    {
        this.immutableInstance = null; // No original immutable state
        this.mutableRowData = new MutableRowData(rowData);
        this.metadata = rowData.Table.Model;
        this.isNew = false;
        // Initialize PK cache from row data
        _cachedPrimaryKey = KeyFactory.CreateKeyFromValues(rowData.GetValues(metadata.Table.PrimaryKeyColumns));
        _isPkCached = true;
    }

    public void Reset()
    {
        mutableRowData.Reset();
        // Re-cache PK based on reset state (original immutable if available)
        _cachedPrimaryKey = immutableInstance?.PrimaryKeys()
           ?? KeyFactory.CreateKeyFromValues(mutableRowData.GetValues(metadata.Table.PrimaryKeyColumns));
        _isPkCached = true;
    }

    public void Reset(T model)
    {
        this.immutableInstance = model; // Update original reference
        mutableRowData.Reset(model.GetRowData());
        isNew = false;
        // Re-cache PK from the new model
        _cachedPrimaryKey = model.PrimaryKeys();
        _isPkCached = true;
    }

    public void Reset(RowData rowData)
    {
        this.immutableInstance = null; // Original immutable state is lost
        mutableRowData.Reset(rowData);
        isNew = false;
        // Re-cache PK from row data
        _cachedPrimaryKey = KeyFactory.CreateKeyFromValues(rowData.GetValues(metadata.Table.PrimaryKeyColumns));
        _isPkCached = true;
    }

    public void SetDeleted() => isDeleted = true;

    public object? GetValue(string propertyName) => mutableRowData.GetValue(metadata.ValueProperties[propertyName].Column);
    public void SetValue<V>(string propertyName, V value) => this[propertyName] = value; // Use indexer to handle PK invalidation

    public bool HasPrimaryKeysSet() => !(PrimaryKeys() is NullKey);

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges() => mutableRowData.GetChanges();
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => mutableRowData.GetColumnAndValues();
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => mutableRowData.GetColumnAndValues(columns);

    public void ClearLazy() => lazyValues?.Clear();

    public void SetLazy<V>(string name, V value)
    {
        lazyValues ??= new ConcurrentDictionary<string, object?>();
        lazyValues[name] = value;
    }

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

    // --- Start of Equality Implementation ---

    public bool Equals(Mutable<T>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        // Compare based on primary key
        return this.PrimaryKeys().Equals(other.PrimaryKeys());
    }

    // Implement IEquatable<T> where T is the Immutable type
    public bool Equals(T? other)
    {
        if (other is null) return false;
        // Compare based on primary key
        return this.PrimaryKeys().Equals(other.PrimaryKeys());
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;

        // Check against Mutable first
        if (obj is Mutable<T> otherMutable) return Equals(otherMutable);

        // Check against Immutable (T)
        if (obj is T otherImmutable) return Equals(otherImmutable);

        return false;
    }

    public override int GetHashCode()
    {
        // Hash code MUST be based *only* on the primary key for consistency and stability
        return PrimaryKeys().GetHashCode();
    }
}