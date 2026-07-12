using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

// Add IEquatable interfaces
public class Mutable<T> : IMutableInstance,
    IEquatable<Mutable<T>>, IEquatable<T>, IMutableLifecycle, IMutableChangeTracking // T is the Immutable type
    where T : class, IImmutableInstance
{
    // Transient ID for distinguishing new instances
    private readonly Guid TransientId;
    private readonly object rowDataMutationOwner = new();

    private readonly ModelDefinition metadata;
    public ModelDefinition Metadata() => metadata;

    protected ConcurrentDictionary<string, object?>? lazyValues = null;
    private T? immutableInstance; // Original immutable state if created from one
    public T? GetImmutableInstance() => immutableInstance;

    private readonly MutableLifecycle lifecycle;
    internal MutableLifecycleSnapshot Lifecycle => lifecycle.Snapshot;
    MutableLifecycleSnapshot IMutableLifecycle.Lifecycle => Lifecycle;
    private DataLinqKey baselineCanonicalPrimaryKey;
    DataLinqKey IMutableLifecycle.BaselineCanonicalPrimaryKey =>
        baselineCanonicalPrimaryKey;
    long IMutableChangeTracking.MutationVersion => mutableRowData.MutationVersion;

    public bool IsNew() => lifecycle.IsNew;
    public bool IsDeleted() => lifecycle.IsDeleted;
    public bool HasChanges() => mutableRowData.HasChanges();

    private MutableRowData mutableRowData;
    public MutableRowData GetRowData() => mutableRowData;
    IRowData IModelInstance.GetRowData() => GetRowData();

    private DataLinqKey? _cachedPrimaryKey = null;
    private bool _isPkCached = false;

    private DataLinqKey GetCurrentPrimaryKey()
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

        if (_isPkCached && _cachedPrimaryKey.HasValue && !pkChanged)
        {
            return _cachedPrimaryKey.Value; // Return cached value
        }

        // 2. Need to calculate/recalculate PK from current data
        _cachedPrimaryKey = KeyFactory.GetKey(mutableRowData, metadata.Table.PrimaryKeyColumns);

        // 3. Determine if the calculated key is actually set.
        //    This replaces the recursive HasPrimaryKeysSet() check
        bool isPkConsideredSet = !_cachedPrimaryKey.Value.IsNull;

        // 4. Only mark as cached if it's considered set OR if the object isn't new
        //    Existing rows cache an all-null key if their PK was somehow nullified.
        _isPkCached = isPkConsideredSet || !this.IsNew();


        return _cachedPrimaryKey.Value;
    }

    // Helper method to check if any PK columns are in the mutated data
    private bool MutatedDataContainsKey(IReadOnlyList<ColumnDefinition> pkColumns)
    {
        // Assuming mutableRowData exposes its internal changes dictionary or a method to check
        // If mutableRowData.MutatedData is accessible:
        return mutableRowData.GetChanges().Any(kvp => pkColumns.Contains(kvp.Key));

        // Or if MutableRowData needs a dedicated method:
        // return mutableRowData.HasPrimaryKeyChanged();
    }

    // Public method remains the same
    public DataLinqKey PrimaryKeys() => GetCurrentPrimaryKey();

    // HasPrimaryKeysSet remains the same, relying on the fixed PrimaryKeys()
    public bool HasPrimaryKeysSet() => !PrimaryKeys().IsNull;

    public object? this[ColumnDefinition column]
    {
        get
        {
            ValidateMappedColumn(column);
            return mutableRowData[column];
        }
        set
        {
            ValidateMappedColumn(column);
            if (metadata.Table.PrimaryKeyColumns.Contains(column))
            {
                _isPkCached = false;
                _cachedPrimaryKey = null;
            }
            mutableRowData.SetValue(column, value, rowDataMutationOwner);
        }
    }

    private void ValidateMappedColumn(ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (column.Index < 0 ||
            column.Index >= metadata.Table.ColumnCount ||
            !ReferenceEquals(metadata.Table.Columns[column.Index], column))
        {
            throw new ArgumentException(
                "The column must be the exact mapped column definition for this mutable model.",
                nameof(column));
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
            mutableRowData.SetValue(column, value, rowDataMutationOwner);
        }
    }

    // Constructor for NEW instances
    public Mutable() : this(ModelDefinition.Find<T>() ?? throw new InvalidOperationException($"Model {typeof(T).Name} not found"))
    {
    }

    protected Mutable(ModelDefinition metadata)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.mutableRowData = new MutableRowData(metadata.Table, rowDataMutationOwner);
        lifecycle = MutableLifecycle.New();
        baselineCanonicalPrimaryKey = DataLinqKey.Null;
        _isPkCached = false;
        _cachedPrimaryKey = null;
        TransientId = Guid.NewGuid();
    }

    // Constructor from EXISTING immutable instance
    public Mutable(T model)
    {
        this.immutableInstance = model;
        this.metadata = model.Metadata();
        this.mutableRowData = new MutableRowData(model.GetRowData(), rowDataMutationOwner);
        lifecycle = MutableLifecycle.FromImmutable(model);
        _cachedPrimaryKey = model.PrimaryKeys();
        baselineCanonicalPrimaryKey = _cachedPrimaryKey.Value;
        _isPkCached = true;
        // Initialize TransientId (though less critical here) ---
        TransientId = Guid.NewGuid();
    }

    // Reset: Clears changes, reverts to original state if available
    public void Reset()
    {
        lifecycle.ValidateAssignmentReset();
        var resetPrimaryKey = immutableInstance is not null
            ? immutableInstance.PrimaryKeys()
            : IsNew()
                ? (DataLinqKey?)null
                : throw new InvalidOperationException(
                    $"Existing mutable model '{typeof(T).FullName}' has no immutable baseline to reset.");

        mutableRowData.Reset(rowDataMutationOwner); // Clears MutatedData
        _cachedPrimaryKey = resetPrimaryKey;
        _isPkCached = resetPrimaryKey is not null;
    }

    // Reset based on a specific immutable instance
    public void Reset(T model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var replacement = MutableBaselineOrigin.FromImmutable(model);
        lifecycle.ValidatePublicBaselineReset(replacement);
        ReplaceBaseline(model);
        lifecycle.ApplyPublicBaselineReset(replacement);
    }

    internal void AdvanceBaseline(
        T model,
        MutableTransactionOwnership owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        lifecycle.ValidateHydratedAdvance();
        ReplaceBaseline(model);
        lifecycle.AdvanceHydrated(owner);
    }

    internal void Invalidate(MutableInvalidationReason reason) =>
        lifecycle.Invalidate(reason);

    void IMutableLifecycle.AdvanceBaseline(
        IImmutableInstance immutable,
        MutableTransactionOwnership owner)
    {
        if (immutable is not T typedImmutable)
        {
            throw new ArgumentException(
                $"Cannot advance mutable model '{typeof(T).FullName}' from immutable model '{immutable.GetType().FullName}'.",
                nameof(immutable));
        }

        AdvanceBaseline(typedImmutable, owner);
    }

    void IMutableLifecycle.MarkDeleted(MutableTransactionOwnership owner) =>
        lifecycle.MarkDeleted(owner);

    void IMutableLifecycle.Invalidate(MutableInvalidationReason reason) =>
        lifecycle.Invalidate(reason);

    private void ReplaceBaseline(T model)
    {
        var replacementRowData = model.GetRowData()
            ?? throw new InvalidOperationException(
                $"Immutable model '{model.GetType().FullName}' returned no row data.");
        if (!ReferenceEquals(replacementRowData.Table, mutableRowData.Table))
        {
            throw new InvalidOperationException(
                $"Cannot reset mutable model '{typeof(T).FullName}' from a different table definition.");
        }

        var replacementPrimaryKey = model.PrimaryKeys();

        immutableInstance = model;
        mutableRowData.Reset(replacementRowData, rowDataMutationOwner);
        _cachedPrimaryKey = replacementPrimaryKey;
        baselineCanonicalPrimaryKey = replacementPrimaryKey;
        _isPkCached = true;
    }

    public void SetDeleted() => lifecycle.MarkDeletedWithoutTransaction();

    public object? GetValue(string propertyName) => mutableRowData.GetValue(metadata.ValueProperties[propertyName].Column);
    public void SetValue<V>(string propertyName, V value) => this[propertyName] = value; // Use indexer to handle PK invalidation
    protected object? GetValue(ColumnDefinition column) => mutableRowData.GetValue(column);
    protected void SetValue<V>(ColumnDefinition column, V value) => this[column] = value; // Use indexer to handle PK invalidation
    
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
