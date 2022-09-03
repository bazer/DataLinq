using System;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.Metadata
{
    public enum IndexType
    {
        Unique
    }

    public class ColumnIndex
    {
        public List<Column> Columns { get; set; } = new List<Column>();
        public IndexType Type { get; set; }
        public string ConstraintName { get; set; }
    }
}
