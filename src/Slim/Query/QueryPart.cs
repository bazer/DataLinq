using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;

namespace Modl.Db.Query
{
    public abstract class QueryPart
        //where M : IDbModl, new()
    {
        public abstract Sql GetCommandString(Sql sql, string prefix, int number);
        public abstract Sql GetCommandParameter(Sql sql, string prefix, int number);

        public QueryPart()
        {
        }
    }
}
