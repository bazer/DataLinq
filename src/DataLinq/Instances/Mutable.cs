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
    // Transient ID for distinguishing new instances
    private readonly Guid TransientId;

    private readonly ModelDefinition metadata;
    public ModelDefinition Metadata() => metadata;

    protected ConcurrentDictionary<string, object?>? lazyValues = null;
    private T? immutableInstance; // Original immutable state if created from one
    public T? GetImmutableInstance() => immutableInstance;

    private bool isNew;
    public bool IsNew() => isNew;
    private bool isDeleted;
    public bool IsDeleted() => isDeleted;
    public bool HasChanges() => mutableRowData.HasChanges();

    private MutableRowData mutableRowData;
    public MutableRowData GetRowData() => mutableRowData;
    IRowData IModelInstance.GetRowData() => GetRowData();

    private IKey? _cachedPrimaryKey = null;
    private bool _isPkCached = false;

    private IKey GetCurrentPrimaryKey()
    {
        // 1. Check if we have a valid cached PK and it hasn't been invalidated
        bool pkChanged = false;
        // Only check for changes if not new AND has changes AND PK was previously cached
        if (!this.IsNew() && _isPkCached && HasChanges())
        {
            var pkColumns = metadata.Table.PrimaryKeyColumns;
            // Check if any of the currently mutated fields affect the primary key
            pkChanged = MutatedDataContainsKey(pkColumns); // Helper method needed
        }

        if (_isPkCached && _cachedPrimaryKey != null && !pkChanged)
        {
            return _cachedPrimaryKey; // Return cached value
        }

        // 2. Need to calculate/recalculate PK from current data
        var currentPkValues = mutableRowData.GetValues(metadata.Table.PrimaryKeyColumns).ToArray();
        _cachedPrimaryKey = KeyFactory.CreateKeyFromValues(currentPkValues);

        // 3. Determine if the calculated key is actually "set" (not NullKey)
        //    This replaces the recursive HasPrimaryKeysSet() check
        bool isPkConsideredSet = !(_cachedPrimaryKey is NullKey);

        // 4. Only mark as cached if it's considered set OR if the object isn't new
        //    (We cache NullKey for existing objects if their PK was somehow nullified,
        //     but don't cache NullKey as the "valid" PK for a brand new object unless values were explicitly set to null)
        _isPkCached = isPkConsideredSet || !this.IsNew();


        return _cachedPrimaryKey;
    }

    // Helper method to check if any PK columns are in the mutated data
    private bool MutatedDataContainsKey(ColumnDefinition[] pkColumns)
    {
        // Assuming mutableRowData exposes its internal changes dictionary or a method to check
        // If mutableRowData.MutatedData is accessible:
        return mutableRowData.GetChanges().Any(kvp => pkColumns.Contains(kvp.Key));

        // Or if MutableRowData needs a dedicated method:
        // return mutableRowData.HasPrimaryKeyChanged();
    }

    // Public method remains the same
    public IKey PrimaryKeys() => GetCurrentPrimaryKey();

    // HasPrimaryKeysSet remains the same, relying on the fixed PrimaryKeys()
    public bool HasPrimaryKeysSet() => !(PrimaryKeys() is NullKey);

    public object? this[ColumnDefinition column]
    {
        get => mutableRowData[column];
        set
        {
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
            if (metadata.Table.PrimaryKeyColumns.Contains(column))
            {
                _isPkCached = false;
                _cachedPrimaryKey = null;
            }
            mutableRowData.SetValue(column, value);
        }
    }

    // Constructor for NEW instances
    public Mutable()
    {
        metadata = ModelDefinition.Find<T>() ?? throw new InvalidOperationException($"Model {typeof(T).Name} not found");
        this.mutableRowData = new MutableRowData(metadata.Table);
        isNew = true;
        _isPkCached = false;
        _cachedPrimaryKey = null;
        TransientId = Guid.NewGuid();
    }

    // Constructor from EXISTING immutable instance
    public Mutable(T model)
    {
        this.immutableInstance = model;
        this.mutableRowData = new MutableRowData(model.GetRowData());
        this.metadata = model.Metadata();
        this.isNew = false; // It's not new, it represents an existing entity
        _cachedPrimaryKey = model.PrimaryKeys();
        _isPkCached = true;
        // Initialize TransientId (though less critical here) ---
        TransientId = Guid.NewGuid();
    }

    // Reset: Clears changes, reverts to original state if available
    public void Reset()
    {
        mutableRowData.Reset(); // Clears MutatedData
                                // If it was created from an immutable, isNew remains false.
                                // If it was created with new(), isNew remains true.
                                // Re-cache PK based on reset state
        _cachedPrimaryKey = immutableInstance?.PrimaryKeys() // Use original PK if possible
            ?? (isNew ? null : KeyFactory.CreateKeyFromValues(mutableRowData.GetValues(metadata.Table.PrimaryKeyColumns))); // Recalc only if not new
        _isPkCached = _cachedPrimaryKey != null;
    }

    // Reset based on a specific immutable instance
    public void Reset(T model)
    {
        this.immutableInstance = model;
        mutableRowData.Reset(model.GetRowData());
        isNew = false; // Definitely not new now
        _cachedPrimaryKey = model.PrimaryKeys();
        _isPkCached = true;
    }

    public void SetDeleted() => isDeleted = true;

    public object? GetValue(string propertyName) => mutableRowData.GetValue(metadata.ValueProperties[propertyName].Column);
    public void SetValue<V>(string propertyName, V value) => this[propertyName] = value; // Use indexer to handle PK invalidation
    
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

        // --- NEW: Handle comparison based on IsNew ---
        if (this.IsNew() && other.IsNew())
        {
            // Both are new, compare by TransientId
            return this.TransientId.Equals(other.TransientId);
        }
        if (this.IsNew() || other.IsNew())
        {
            // One is new, the other isn't - they cannot be equal
            return false;
        }
        // --- End NEW ---

        // Neither is new, compare by primary key
        return this.PrimaryKeys().Equals(other.PrimaryKeys());
    }

    public bool Equals(T? other) // T is the Immutable type
    {
        if (other is null) return false;

        // --- NEW: A new mutable cannot equal an existing immutable ---
        if (this.IsNew())
        {
            return false;
        }
        // --- End NEW ---

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
        // Note: Ensure T is constrained appropriately if comparing directly like this
        // If T might have subclasses, a type check might be needed.
        // Assuming T is the direct Immutable type here.
        if (obj is T otherImmutable) return Equals(otherImmutable);

        return false;
    }

    public override int GetHashCode()
    {
        if (this.IsNew())
        {
            // Use the TransientId hash code for unsaved objects
            return TransientId.GetHashCode();
        }
        else
        {
            // Use the Primary Key hash code for saved objects
            return PrimaryKeys().GetHashCode();
        }
    }
}