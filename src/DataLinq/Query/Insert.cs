﻿using System;
using System.Data;
using System.Linq;

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
            return sql.AddFormat("VALUES (NULL)");

        sql.AddFormat("({0}) VALUES (", string.Join(",", query.SetList.Keys.Select(x => $"{query.EscapeCharacter}{x}{query.EscapeCharacter}")));

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

        sql.AddFormat("INSERT INTO ");
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        sql.AddText(" ");
        GetSet(sql, paramPrefix);

        if (query.LastIdQuery)
            sql.AddFormat(";\n{0}", query.DataSource.Provider.GetLastIdQuery());

        return sql;
    }

    public IDbCommand ToDbCommand()
    {
        return query.DataSource.Provider.ToDbCommand(this);
    }

    public QueryResult Execute()
    {
        throw new NotImplementedException();
    }
}