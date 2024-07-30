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

    public Sql ToSql(string? paramPrefix = null)
    {
        var sql = new Sql();

        sql.AddText("UPDATE ");
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        sql.AddText(" SET ");
        query.GetSet(sql, paramPrefix);
        sql.AddText(" \n");
        query.GetWhere(sql, paramPrefix);

        return sql;
    }

    public QueryResult Execute()
    {
        throw new NotImplementedException();
    }
}