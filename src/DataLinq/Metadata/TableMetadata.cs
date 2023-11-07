using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Xml.Linq;
using DataLinq.Attributes;
using DataLinq.Cache;

namespace DataLinq.Metadata
{
    public enum TableType
    {
        Table,
        View
    }

    public class TableMetadata
    {
        private List<Column> primaryKeyColumns;
        public List<Column> Columns { get; set; }
        public DatabaseMetadata Database { get; set; }
        public string DbName { get; set; }
        public ModelMetadata Model { get; set; }

        public List<Column> PrimaryKeyColumns =>
            primaryKeyColumns ??= Columns.Where(x => x.PrimaryKey).ToList();

        public List<ColumnIndex> ColumnIndices { get; set; } = new List<ColumnIndex>();

        public TableType Type { get; protected set; } = TableType.Table;
        public List<(CacheLimitType limitType, long amount)> CacheLimits { get; set; } = new();
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
}