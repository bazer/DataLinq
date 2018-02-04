using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Metadata
{
    public class Constraint
    {
        public string Name { get; set; }
        public Column Column { get; set; }
        public Column ReferencedColumn { get; set; }
    }
}
