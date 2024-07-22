using System;
using System.Data;

namespace DataLinq.Query;

public class Delete<T> : IQuery
{
    private readonly SqlQuery<T> query;

    public Delete(SqlQuery<T> query)
    {
        this.query = query;
    }

    public IDbCommand ToDbCommand()
    {
        throw new System.NotImplementedException();
    }

    public Sql ToSql(string? paramPrefix = null)
    {
        var sql = new Sql();

        sql.AddText("DELETE FROM ");
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        query.GetWhere(sql, paramPrefix);

        return sql;
    }

    public QueryResult Execute()
    {
        throw new NotImplementedException();
    }
}