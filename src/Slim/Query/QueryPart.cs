using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;

namespace Slim.Query
{
    public abstract class QueryPart
    {
        public abstract Sql GetCommandString(Sql sql, string prefix, int number);
        public abstract Sql GetCommandParameter(Sql sql, string prefix, int number);
    }
}
