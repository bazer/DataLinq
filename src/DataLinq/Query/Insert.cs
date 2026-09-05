using System;
using System.Data;
using System.ComponentModel;

namespace DataLinq.Query;

public class Insert<T> : IQuery
{
    private readonly SqlQuery<T> query;

    public Insert(SqlQuery<T> query)
    {
        this.query = query;
    }

    protected Sql GetSet(Sql sql, string? paramPrefix)
    {
        int length = query.SetList.Count;
        if (length == 0)
            return sql.AddText(
                query.DataSource.Provider.Constants.DefaultValuesInsertClause ??
                "VALUES (NULL)");

        sql.AddText("(");
        var columnIndex = 0;
        foreach (var key in query.SetList.Keys)
        {
            if (columnIndex > 0)
                sql.AddText(",");

            sql.AddText(query.EscapeCharacter);
            sql.AddText(key);
            sql.AddText(query.EscapeCharacter);
            columnIndex++;
        }
        sql.AddText(") VALUES (");

        int i = 0;
        foreach (var with in query.SetList)
        {
            query.DataSource.Provider.GetParameter(sql, paramPrefix + "v" + i, with.Value);
            query.DataSource.Provider.GetParameterValue(sql, paramPrefix + "v" + i);

            if (i + 1 < length)
                sql.AddText(",");
            else
                sql.AddText(")");

            i++;
        }

        return sql;
    }

    public Sql ToSql(string? paramPrefix = null)
    {
        var sql = new Sql();

        sql.AddText("INSERT INTO ");
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        sql.AddText(" ");
        GetSet(sql, paramPrefix);

        if (query.LastIdQuery)
            sql.AddText(";\n").AddText(query.DataSource.Provider.GetLastIdQuery());

        return sql;
    }

    public IDbCommand ToDbCommand()
    {
        return query.DataSource.Provider.ToDbCommand(this);
    }

    [Obsolete("Direct query mutation execution is unsupported. Use Transaction.Insert(model) for tracked mutations, or ToDbCommand() for caller-owned raw SQL execution.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public QueryResult Execute()
    {
        throw new NotSupportedException("Use Transaction.Insert(model) for tracked mutations, or ToDbCommand() for caller-owned raw SQL execution.");
    }
}
