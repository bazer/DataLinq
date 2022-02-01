﻿using System.Collections.Generic;
using System.Linq;
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
            primaryKeyColumns ?? (primaryKeyColumns = Columns.Where(x => x.PrimaryKey).ToList());

        public TableType Type { get; set; }
        public List<(CacheLimitType limitType, long amount)> CacheLimits { get; set; } = new();
        public bool UseCache 
        { 
            get => explicitUseCache.HasValue ? explicitUseCache.Value : Database.UseCache; 
            set => explicitUseCache = value; 
        }

        internal bool? explicitUseCache;
    }
}