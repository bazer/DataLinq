using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Attributes;

namespace DataLinq.Metadata;

public enum TableType
{
    Table,
    View
}

public class TableMetadata
{
    //private List<Column> primaryKeyColumns;
    public List<Column> Columns { get; set; }
    public DatabaseMetadata Database { get; set; }
    public string DbName { get; set; }
    public ModelMetadata Model { get; set; }

    public Column[] PrimaryKeyColumns { get; private set; } = [];
    //public List<Column> PrimaryKeyColumns =>
    //    primaryKeyColumns ??= Columns.Where(x => x.PrimaryKey).ToList();

    public List<ColumnIndex> ColumnIndices { get; set; } = new List<ColumnIndex>();

    public TableType Type { get; protected set; } = TableType.Table;
    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; set; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; set; } = [];

    public void AddPrimaryKeyColumn(Column column)
    {
        if (PrimaryKeyColumns == null)
            PrimaryKeyColumns = [column];
        else
            PrimaryKeyColumns = PrimaryKeyColumns.Concat(new[] { column }).ToArray();
    }

    public void RemovePrimaryKeyColumn(Column column)
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

        if (Model?.CsTypeName != DbName)
            desc += $" ({Model.CsTypeName})";

        return desc;
    }
}

public class ViewMetadata : TableMetadata
{
    public string Definition { get; set; }

    public ViewMetadata()
    {
        Type = TableType.View;
    }
}