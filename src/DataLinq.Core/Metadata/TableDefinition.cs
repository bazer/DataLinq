using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using DataLinq.Attributes;

namespace DataLinq.Metadata;

public enum TableType
{
    Table,
    View
}

public class TableDefinition
{
    //private List<Column> primaryKeyColumns;
    public ColumnDefinition[] Columns { get; set; }
    public DatabaseDefinition Database { get; set; }
    public string DbName { get; set; }
    public ModelDefinition Model { get; set; }

    public ColumnDefinition[] PrimaryKeyColumns { get; private set; } = [];
    //public List<Column> PrimaryKeyColumns =>
    //    primaryKeyColumns ??= Columns.Where(x => x.PrimaryKey).ToList();

    public List<ColumnIndex> ColumnIndices { get; set; } = new List<ColumnIndex>();

    public TableType Type { get; protected set; } = TableType.Table;
    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; set; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; set; } = [];

    public void AddPrimaryKeyColumn(ColumnDefinition column)
    {
        if (PrimaryKeyColumns == null)
            PrimaryKeyColumns = [column];
        else
            PrimaryKeyColumns = PrimaryKeyColumns.Concat(new[] { column }).ToArray();
    }

    public void RemovePrimaryKeyColumn(ColumnDefinition column)
    {
        if (PrimaryKeyColumns == null)
            return;

        PrimaryKeyColumns = PrimaryKeyColumns.Where(x => x != column).ToArray();
    }

    public bool UseCache
    {
        get => explicitUseCache.HasValue ? explicitUseCache.Value : Database.UseCache;
        set => explicitUseCache = value;
    }

    internal bool? explicitUseCache;

    public override string ToString()
    {
        var desc = $"Table: {DbName}";

        if (Model?.CsType.Name != DbName)
            desc += $" ({Model?.CsType.Name})";

        return desc;
    }
}

public class ViewDefinition : TableDefinition
{
    public string Definition { get; set; }

    public ViewDefinition()
    {
        Type = TableType.View;
    }
}