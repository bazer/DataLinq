using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using CommunityToolkit.HighPerformance;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Query;

public class Select<T> : IQuery
{
    protected readonly SqlQuery<T> query;
    public SqlQuery<T> Query => query;

    public Select(SqlQuery<T> query)
    {
        this.query = query;
    }

    public Sql ToSql(string? paramPrefix = null)
    {
        var columns = (query.WhatList ?? query.Table.Columns.Select(x => $"{query.EscapeCharacter}{x.DbName}{query.EscapeCharacter}"))
            .Select(x => $"{(!string.IsNullOrWhiteSpace(query.Alias) ? $"{query.Alias}." : "")}{x}")
            .ToJoinedString(", ");

        var sql = new Sql().AddFormat($"SELECT {columns} FROM ");
        query.AddTableName(sql, query.Table.DbName, query.Alias);
        query.GetJoins(sql, paramPrefix);
        query.GetWhere(sql, paramPrefix);
        query.GetOrderBy(sql);
        query.GetLimit(sql);

        return sql;
    }

    public IDbCommand ToDbCommand()
    {
        return query.DataSource.Provider.ToDbCommand(this);
    }

    public Select<T> What(IEnumerable<ColumnDefinition> columns)
    {
        query.What(columns);

        return this;
    }

    public Select<T> What(params string[] selectors)
    {
        query.What(selectors);

        return this;
    }

    public IEnumerable<IDataLinqDataReader> ReadReader()
    {
        return query.DataSource
            .DatabaseAccess
            .ReadReader(query.DataSource.Provider.ToDbCommand(this));
    }

    public IEnumerable<RowData> ReadRows()
    {
        return ReadReader()
            .Select(x => new RowData(x, query.Table, query.Table.Columns.AsSpan()));
    }

    public IEnumerable<IKey> ReadKeys()
    {
        return KeyFactory.GetKeys(this, query.Table.PrimaryKeyColumns);
    }

    //public IEnumerable<IKey> ReadForeignKeys(ColumnIndex foreignKeyIndex)
    //{
    //    return ReadReader()
    //        .Select(x => new RowData(x, query.Table, foreignKeyIndex.Columns.AsSpan()))
    //        .Select(x => new ForeignKey(foreignKeyIndex, x.GetValues(foreignKeyIndex.Columns).ToArray()));
    //}

    public IEnumerable<(IKey fk, IKey[] pks)> ReadPrimaryAndForeignKeys(ColumnIndex foreignKeyIndex)
    {
        return ReadReader()
            .Select(x => new RowData(x, query.Table, query.Table.PrimaryKeyColumns.Concat(foreignKeyIndex.Columns).Distinct().ToArray()))
            .Select(x => (fk: KeyFactory.CreateKeyFromValues(x.GetValues(foreignKeyIndex.Columns)), pk: KeyFactory.GetKey(x, query.Table.PrimaryKeyColumns)))
            .GroupBy(x => x.fk)
            .Select(x => (x.Key, x.Select(y => y.pk).ToArray()));
    }

    public IEnumerable<V> ExecuteAs<V>() =>
        Execute().Select(x => (V)x);

    public IEnumerable<IImmutableInstance> Execute()
    {
        if (query.Table.PrimaryKeyColumns.Length != 0)
        {
            this.What(query.Table.PrimaryKeyColumns);

            var keys = this
                .ReadKeys()
                .ToArray();

            foreach (var row in query.DataSource.Provider.GetTableCache(query.Table).GetRows(keys, query.DataSource, orderings: query.OrderByList))
                yield return row;
        }
        else
        {
            var rows = this
                .ReadRows()
                .Select(x => InstanceFactory.NewImmutableRow(x, query.DataSource));

            foreach (var row in rows)
                yield return row;
        }
    }

    public V ExecuteScalar<V>()
    {
        return query.DataSource.DatabaseAccess.ExecuteScalar<V>(query.DataSource.Provider.ToDbCommand(this));
    }

    public object? ExecuteScalar()
    {
        return query.DataSource.DatabaseAccess.ExecuteScalar(query.DataSource.Provider.ToDbCommand(this));
    }

    public override string ToString()
    {
        return ToSql().ToString();
    }
}