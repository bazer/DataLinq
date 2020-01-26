using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;

namespace Slim.Query
{
    public abstract class QueryPart
    {
        public abstract void GetCommandString(Sql sql, string prefix, bool addCommandParameter = true);
        //protected abstract void GetCommandParameter(Sql sql, string prefix);
    }
}
