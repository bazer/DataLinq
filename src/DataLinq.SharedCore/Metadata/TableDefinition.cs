using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public enum TableType
{
    Table,
    View
}

public class TableDefinition(string dbName) : IDefinition
{
    private MetadataCollection<ColumnDefinition> columns = MetadataCollection<ColumnDefinition>.Empty;
    private MetadataCollection<ColumnDefinition> primaryKeyColumns = MetadataCollection<ColumnDefinition>.Empty;
    private Dictionary<string, ColumnDefinition>? columnsByDbName;
    private Dictionary<string, ColumnDefinition>? columnsByDbNameIgnoreCase;
    private Dictionary<string, ColumnDefinition>? columnsByPropertyName;
    private Dictionary<ColumnDefinition, MetadataCollection<ColumnIndex>>? columnIndicesByColumn;
    private ColumnDefinition? autoIncrementPrimaryKeyColumn;

    public string DbName { get; private set; } = dbName;
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetDbName(string dbName)
    {
        SetDbNameCore(dbName);
    }

    internal void SetDbNameCore(string dbName)
    {
        ThrowIfFrozen();
        DbName = dbName;
    }

    public TableModel TableModel { get; private set; } = null!;

    internal void SetTableModel(TableModel tableModel)
    {
        ThrowIfFrozen();
        TableModel = tableModel;
    }

    public DatabaseDefinition Database => TableModel.Database;
    public ModelDefinition Model => TableModel.Model;
    public MetadataCollection<ColumnDefinition> Columns => columns;
    public int ColumnCount => columns.Length;

    public ColumnDefinition GetColumn(int ordinal) => columns[ordinal];

    public ColumnDefinition GetColumnByDbName(string dbName)
    {
        return GetColumnByDbName(dbName, StringComparison.Ordinal);
    }

    public ColumnDefinition GetColumnByDbName(string dbName, StringComparison comparison)
    {
        if (TryGetColumnByDbName(dbName, comparison, out var column))
            return column;

        throw new KeyNotFoundException($"No column named '{dbName}' exists on table '{DbName}'.");
    }

    public bool TryGetColumnByDbName(string dbName, out ColumnDefinition column)
    {
        return TryGetColumnByDbName(dbName, StringComparison.Ordinal, out column);
    }

    public bool TryGetColumnByDbName(string dbName, StringComparison comparison, out ColumnDefinition column)
    {
        if (dbName is null)
            throw new ArgumentNullException(nameof(dbName));

        var lookup = comparison == StringComparison.OrdinalIgnoreCase
            ? columnsByDbNameIgnoreCase
            : comparison == StringComparison.Ordinal
                ? columnsByDbName
                : null;

        if (lookup is not null)
        {
            if (lookup.TryGetValue(dbName, out column!))
                return true;

            column = null!;
            return false;
        }

        foreach (var candidate in columns)
        {
            if (string.Equals(candidate.DbName, dbName, comparison))
            {
                column = candidate;
                return true;
            }
        }

        column = null!;
        return false;
    }

    public ColumnDefinition GetColumnByPropertyName(string propertyName)
    {
        if (TryGetColumnByPropertyName(propertyName, out var column))
            return column;

        throw new KeyNotFoundException($"No column for property '{propertyName}' exists on table '{DbName}'.");
    }

    public bool TryGetColumnByPropertyName(string propertyName, out ColumnDefinition column)
    {
        if (propertyName is null)
            throw new ArgumentNullException(nameof(propertyName));

        if (columnsByPropertyName is not null &&
            columnsByPropertyName.TryGetValue(propertyName, out column!))
            return true;

        column = null!;
        return false;
    }

    public MetadataCollection<ColumnIndex> GetColumnIndices(ColumnDefinition column)
    {
        if (column is null)
            throw new ArgumentNullException(nameof(column));

        if (IsFrozen &&
            columnIndicesByColumn is not null &&
            columnIndicesByColumn.TryGetValue(column, out var frozenIndices))
            return frozenIndices;

        if (IsFrozen)
            return MetadataCollection<ColumnIndex>.Empty;

        var indices = new List<ColumnIndex>();
        for (var i = 0; i < ColumnIndices.Count; i++)
        {
            var index = ColumnIndices[i];
            if (index.Columns.Contains(column))
                indices.Add(index);
        }

        return new MetadataCollection<ColumnIndex>(indices);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetColumns(IEnumerable<ColumnDefinition> columns)
    {
        SetColumnsCore(columns);
    }

    internal void SetColumnsCore(IEnumerable<ColumnDefinition> columns)
    {
        ThrowIfFrozen();
        this.columns = new MetadataCollection<ColumnDefinition>(columns);
        RebuildColumnLookups();
        columnIndicesByColumn = null;
    }

    public MetadataCollection<ColumnDefinition> PrimaryKeyColumns => primaryKeyColumns;
    public bool HasAutoIncrementPrimaryKey => AutoIncrementPrimaryKeyColumn is not null;
    public ColumnDefinition? AutoIncrementPrimaryKeyColumn =>
        IsFrozen
            ? autoIncrementPrimaryKeyColumn
            : FindAutoIncrementPrimaryKeyColumn();

    public MetadataList<ColumnIndex> ColumnIndices { get; } = new();

    public TableType Type { get; protected set; } = TableType.Table;
    public MetadataList<(CacheLimitType limitType, long amount)> CacheLimits { get; } = new();
    public MetadataList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; } = new();
    public CsFileDeclaration? CsFile => Model?.CsFile;

    internal bool? explicitUseCache;
    public bool UseCache
    {
        get => explicitUseCache ?? Database.UseCache;
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetUseCacheCore(value);
        }
    }

    internal void SetUseCacheCore(bool useCache)
    {
        ThrowIfFrozen();
        explicitUseCache = useCache;
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddPrimaryKeyColumn(ColumnDefinition column)
    {
        AddPrimaryKeyColumnCore(column);
    }

    internal void AddPrimaryKeyColumnCore(ColumnDefinition column)
    {
        ThrowIfFrozen();

        if (primaryKeyColumns.Contains(column))
            throw DLOptionFailure.Exception(DLFailureType.InvalidArgument, $"Column {column} already in primary key");
        else
            primaryKeyColumns = new MetadataCollection<ColumnDefinition>(primaryKeyColumns.Append(column));
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void RemovePrimaryKeyColumn(ColumnDefinition column)
    {
        RemovePrimaryKeyColumnCore(column);
    }

    internal void RemovePrimaryKeyColumnCore(ColumnDefinition column)
    {
        ThrowIfFrozen();

        primaryKeyColumns = new MetadataCollection<ColumnDefinition>(primaryKeyColumns.Where(x => x != column));
    }


    public override string ToString()
    {
        var desc = $"Table: {DbName}";

        if (Model?.CsType.Name != DbName)
            desc += $" ({Model?.CsType.Name})";

        return desc;
    }

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        RebuildColumnLookups();
        RebuildColumnIndexLookups();
        autoIncrementPrimaryKeyColumn = FindAutoIncrementPrimaryKeyColumn();
        IsFrozen = true;

        foreach (var column in columns)
            column.Freeze();

        foreach (var index in ColumnIndices)
            index.Freeze();

        ColumnIndices.Freeze();
        CacheLimits.Freeze();
        IndexCache.Freeze();
    }

    private void RebuildColumnLookups()
    {
        var byDbName = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal);
        var byDbNameIgnoreCase = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);
        var byPropertyName = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal);

        foreach (var column in columns)
        {
            if (column is null)
                continue;

            if (!byDbName.ContainsKey(column.DbName))
                byDbName.Add(column.DbName, column);

            if (!byDbNameIgnoreCase.ContainsKey(column.DbName))
                byDbNameIgnoreCase.Add(column.DbName, column);

            if (column.ValueProperty is not null && !byPropertyName.ContainsKey(column.ValueProperty.PropertyName))
                byPropertyName.Add(column.ValueProperty.PropertyName, column);
        }

        columnsByDbName = byDbName;
        columnsByDbNameIgnoreCase = byDbNameIgnoreCase;
        columnsByPropertyName = byPropertyName;
    }

    private void RebuildColumnIndexLookups()
    {
        var mutableLookup = new Dictionary<ColumnDefinition, List<ColumnIndex>>();

        for (var indexPosition = 0; indexPosition < ColumnIndices.Count; indexPosition++)
        {
            var index = ColumnIndices[indexPosition];
            for (var columnPosition = 0; columnPosition < index.Columns.Count; columnPosition++)
            {
                var column = index.Columns[columnPosition];
                if (!mutableLookup.TryGetValue(column, out var indices))
                {
                    indices = [];
                    mutableLookup.Add(column, indices);
                }

                indices.Add(index);
            }
        }

        var frozenLookup = new Dictionary<ColumnDefinition, MetadataCollection<ColumnIndex>>();
        foreach (var item in mutableLookup)
            frozenLookup.Add(item.Key, new MetadataCollection<ColumnIndex>(item.Value));

        columnIndicesByColumn = frozenLookup;
    }

    private ColumnDefinition? FindAutoIncrementPrimaryKeyColumn()
    {
        for (var i = 0; i < primaryKeyColumns.Count; i++)
        {
            var column = primaryKeyColumns[i];
            if (column.AutoIncrement)
                return column;
        }

        return null;
    }

    protected void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}

public class ViewDefinition : TableDefinition
{
    public string? Definition { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetDefinition(string definition)
    {
        SetDefinitionCore(definition);
    }

    internal void SetDefinitionCore(string definition)
    {
        ThrowIfFrozen();
        Definition = definition;
    }

    public ViewDefinition(string dbName) : base(dbName)
    {
        Type = TableType.View;
    }
}
