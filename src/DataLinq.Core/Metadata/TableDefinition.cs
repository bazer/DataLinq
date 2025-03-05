using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public enum TableType
{
    Table,
    View
}

public class TableDefinition(string dbName) : IDefinition
{
    public string DbName { get; private set; } = dbName;
    public void SetDbName(string dbName) => DbName = dbName;
    public TableModel TableModel { get; private set; }
    internal void SetTableModel(TableModel tableModel) => TableModel = tableModel;
    public DatabaseDefinition Database => TableModel.Database;
    public ModelDefinition Model => TableModel.Model;
    public ColumnDefinition[] Columns { get; private set; } = [];
    public void SetColumns(IEnumerable<ColumnDefinition> columns) => Columns = columns.ToArray();

    public ColumnDefinition[] PrimaryKeyColumns { get; private set; } = [];
    public List<ColumnIndex> ColumnIndices { get; private set; } = [];

    public TableType Type { get; protected set; } = TableType.Table;
    public List<(CacheLimitType limitType, long amount)> CacheLimits { get; } = [];
    public List<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; } = [];
    public CsFileDeclaration? CsFile => Model?.CsFile;

    internal bool? explicitUseCache;
    public bool UseCache
    {
        get => explicitUseCache ?? Database.UseCache;
        set => explicitUseCache = value;
    }


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
    public string? Definition { get; private set; }
    public void SetDefinition(string definition) => Definition = definition;

    public ViewDefinition(string dbName) : base(dbName)
    {
        Type = TableType.View;
    }
}