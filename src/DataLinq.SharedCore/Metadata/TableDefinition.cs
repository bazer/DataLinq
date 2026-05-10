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
    private Dictionary<string, ColumnDefinition> columnsByDbName = new(StringComparer.Ordinal);
    private Dictionary<string, ColumnDefinition> columnsByPropertyName = new(StringComparer.Ordinal);

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
        if (TryGetColumnByDbName(dbName, out var column))
            return column;

        throw new KeyNotFoundException($"No column named '{dbName}' exists on table '{DbName}'.");
    }

    public bool TryGetColumnByDbName(string dbName, out ColumnDefinition column)
    {
        if (dbName is null)
            throw new ArgumentNullException(nameof(dbName));

        return columnsByDbName.TryGetValue(dbName, out column!);
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

        return columnsByPropertyName.TryGetValue(propertyName, out column!);
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
    }

    public MetadataCollection<ColumnDefinition> PrimaryKeyColumns => primaryKeyColumns;
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
        var byPropertyName = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal);

        foreach (var column in columns)
        {
            if (column is null)
                continue;

            if (!byDbName.ContainsKey(column.DbName))
                byDbName.Add(column.DbName, column);

            if (column.ValueProperty is not null && !byPropertyName.ContainsKey(column.ValueProperty.PropertyName))
                byPropertyName.Add(column.ValueProperty.PropertyName, column);
        }

        columnsByDbName = byDbName;
        columnsByPropertyName = byPropertyName;
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
