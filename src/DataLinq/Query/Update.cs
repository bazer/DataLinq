using System;
using System.Data;
using System.ComponentModel;

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
        return query.DataSource.Provider.ToDbCommand(this);
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

    [Obsolete("Direct query mutation execution is unsupported. Use Transaction.Update(model) for tracked mutations, or ToDbCommand() for caller-owned raw SQL execution.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public QueryResult Execute()
    {
        throw new NotSupportedException("Use Transaction.Update(model) for tracked mutations, or ToDbCommand() for caller-owned raw SQL execution.");
    }
}
