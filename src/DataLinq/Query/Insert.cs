using System;
using System.Data;

namespace DataLinq.Query;

public class Insert<T> : IQuery
{
    private readonly SqlQuery<T> query;

    public Insert(SqlQuery<T> query)
    {
        this.query = query;
    }

    protected Sql GetSet(Sql sql, string paramPrefix)
    {
        int length = query.SetList.Count;
        if (length == 0)
            return sql.AddFormat("VALUES (NULL)");

        sql.AddFormat("({0}) VALUES (", string.Join(",", query.SetList.Keys));

        int i = 0;
        foreach (var with in query.SetList)
        {
            query.Transaction.Provider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
            query.Transaction.Provider.GetParameterValue(sql, paramPrefix + "v" + i);

            if (i + 1 < length)
                sql.AddText(",");
            else
                sql.AddText(")");

            i++;
        }

        return sql;
    }

    public Sql ToSql(string paramPrefix = null)
    {
        var sql = GetSet(
            new Sql().AddFormat("INSERT INTO {0} ", query.Table.DbName),
            paramPrefix);

        if (query.LastIdQuery)
            sql.AddFormat(";\n{0}", query.Transaction.Provider.GetLastIdQuery());

        return sql;
    }

    public IDbCommand ToDbCommand()
    {
        return query.Transaction.Provider.ToDbCommand(this);
    }

    public QueryResult Execute()
    {
        throw new NotImplementedException();
    }
}