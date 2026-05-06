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
    private ColumnDefinition[] columns = [];
    private ColumnDefinition[] primaryKeyColumns = [];

    public string DbName { get; private set; } = dbName;
    public bool IsFrozen { get; private set; }

    public void SetDbName(string dbName)
    {
        ThrowIfFrozen();
        DbName = dbName;
    }

    public TableModel TableModel { get; private set; }

    internal void SetTableModel(TableModel tableModel)
    {
        ThrowIfFrozen();
        TableModel = tableModel;
    }

    public DatabaseDefinition Database => TableModel.Database;
    public ModelDefinition Model => TableModel.Model;
    public ColumnDefinition[] Columns => columns.ToArray();

    public void SetColumns(IEnumerable<ColumnDefinition> columns)
    {
        ThrowIfFrozen();
        this.columns = columns.ToArray();
    }

    public ColumnDefinition[] PrimaryKeyColumns => primaryKeyColumns.ToArray();
    public MetadataList<ColumnIndex> ColumnIndices { get; } = new();

    public TableType Type { get; protected set; } = TableType.Table;
    public MetadataList<(CacheLimitType limitType, long amount)> CacheLimits { get; } = new();
    public MetadataList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; } = new();
    public CsFileDeclaration? CsFile => Model?.CsFile;

    internal bool? explicitUseCache;
    public bool UseCache
    {
        get => explicitUseCache ?? Database.UseCache;
        set
        {
            ThrowIfFrozen();
            explicitUseCache = value;
        }
    }


    public void AddPrimaryKeyColumn(ColumnDefinition column)
    {
        ThrowIfFrozen();

        if (primaryKeyColumns == null)
            primaryKeyColumns = [column];
        else if (primaryKeyColumns.Contains(column))
            throw DLOptionFailure.Exception(DLFailureType.InvalidArgument, $"Column {column} already in primary key");
        else
            primaryKeyColumns = primaryKeyColumns.Concat(new[] { column }).ToArray();
    }

    public void RemovePrimaryKeyColumn(ColumnDefinition column)
    {
        ThrowIfFrozen();

        if (primaryKeyColumns == null)
            return;

        primaryKeyColumns = primaryKeyColumns.Where(x => x != column).ToArray();
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

        IsFrozen = true;

        foreach (var column in columns)
            column.Freeze();

        foreach (var index in ColumnIndices)
            index.Freeze();

        ColumnIndices.Freeze();
        CacheLimits.Freeze();
        IndexCache.Freeze();
    }

    protected void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}

public class ViewDefinition : TableDefinition
{
    public string? Definition { get; private set; }

    public void SetDefinition(string definition)
    {
        ThrowIfFrozen();
        Definition = definition;
    }

    public ViewDefinition(string dbName) : base(dbName)
    {
        Type = TableType.View;
    }
}
