using System;
using System.Data;

namespace DataLinq.Query;

public class Update<T> : IQuery
{
    private readonly SqlQuery<T> query;

    public Update(SqlQuery<T> query)
    {
        this.query = query;
    }

    public IDbCommand ToDbCommand()
    {
        throw new System.NotImplementedException();
    }

    public Sql ToSql(string paramPrefix = null)
    {
        var sql = query.GetSet(
            new Sql().AddFormat("UPDATE {0} SET ", query.DbName),
            paramPrefix);

        return query.GetWhere(
            sql.AddText(" \n"),
            paramPrefix);
    }

    public QueryResult Execute()
    {
        throw new NotImplementedException();
    }
}