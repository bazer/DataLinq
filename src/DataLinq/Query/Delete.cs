using System;
using System.Data;
using System.ComponentModel;

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
        return query.DataSource.Provider.ToDbCommand(this);
    }

    public Sql ToSql(string? paramPrefix = null)
    {
        var sql = new Sql();

        sql.AddText("DELETE FROM ");
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        query.GetWhere(sql, paramPrefix);

        return sql;
    }

    [Obsolete("Direct query mutation execution is unsupported. Use Transaction.Delete(model) for tracked mutations, or ToDbCommand() for caller-owned raw SQL execution.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public QueryResult Execute()
    {
        throw new NotSupportedException("Use Transaction.Delete(model) for tracked mutations, or ToDbCommand() for caller-owned raw SQL execution.");
    }
}
