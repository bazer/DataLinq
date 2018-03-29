using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Metadata
{
    public class Constraint
    {
        public Column Column { get; set; }
        public string ColumnName { get; set; }
        public string Name { get; set; }
        public Column ReferencedColumn { get; set; }
        public string ReferencedColumnName { get; set; }
        public string ReferencedTableName { get; set; }
    }
}
