using System;
using System.Collections.Generic;

namespace Slim.Metadata
{
    public enum TableType
    {
        Table,
        View
    }

    public class Table
    {
        public List<Column> Columns { get; set; }
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
        public Database Database { get; set; }
        public string DbName { get; set; }
        public TableType Type { get; set; }
    }
}