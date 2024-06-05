using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Query;

public class Select<T> : IQuery
{
    protected List<Column> columnsToSelect;

    protected readonly SqlQuery<T> query;

    public Select(SqlQuery<T> query)
    {
        this.query = query;
    }

    public Sql ToSql(string paramPrefix = null)
    {
        var columns = (query.WhatList ?? query.Table.Columns)
            .Select(x => $"{(!string.IsNullOrWhiteSpace(query.Alias) ? $"{query.Alias}." : "")}{query.EscapeCharacter}{x.DbName}{query.EscapeCharacter}")
            .ToJoinedString(", ");

        var sql = new Sql().AddFormat($"SELECT {columns} FROM {query.DbName}");
        query.GetJoins(sql, paramPrefix);
        query.GetWhere(sql, paramPrefix);
        query.GetOrderBy(sql);
        query.GetLimit(sql);

        return sql;
    }

    public IDbCommand ToDbCommand()
    {
        return query.Transaction.Provider.ToDbCommand(this);
    }

    public Select<T> What(IEnumerable<Column> columns)
    {
        columnsToSelect = columns.ToList();

        return this;
    }

    public IEnumerable<RowData> ReadRows()
    {
        return query.Transaction
            .DatabaseTransaction
            .ReadReader(query.Transaction.Provider.ToDbCommand(this))
            .Select(x => new RowData(x, query.Table, query.Table.Columns));
    }

    public IEnumerable<PrimaryKeys> ReadKeys()
    {
        return query.Transaction
            .DatabaseTransaction
            .ReadReader(query.Transaction.Provider.ToDbCommand(this))
            .Select(x => new PrimaryKeys(x, query.Table));
    }

    public IEnumerable<ForeignKey> ReadForeignKeys(ColumnIndex foreignKeyIndex)
    {
        return query.Transaction
            .DatabaseTransaction
            .ReadReader(query.Transaction.Provider.ToDbCommand(this))
            .Select(x => new RowData(x, query.Table, foreignKeyIndex.Columns))
            .Select(x => new ForeignKey(foreignKeyIndex, x.GetValues(foreignKeyIndex.Columns).ToArray()));
    }

    public IEnumerable<(ForeignKey, PrimaryKeys[])> ReadPrimaryAndForeignKeys(ColumnIndex foreignKeyIndex)
    {
        return query.Transaction
            .DatabaseTransaction
            .ReadReader(query.Transaction.Provider.ToDbCommand(this))
            .Select(x => new RowData(x, query.Table, query.Table.PrimaryKeyColumns.Concat(foreignKeyIndex.Columns).Distinct().ToList()))
            .Select(x => (fk: new ForeignKey(foreignKeyIndex, x.GetValues(foreignKeyIndex.Columns).ToArray()), pk: new PrimaryKeys(x)))
            .GroupBy(x => x.fk)
            .Select(x => (x.Key, x.Select(y => y.pk).ToArray()));
    }

    //public IEnumerable<T> Execute()
    //{
    //    if (query.Table.PrimaryKeyColumns.Count != 0)
    //    {
    //        query.What(query.Table.PrimaryKeyColumns);

    //        var keys = this
    //            .ReadKeys()
    //            .ToArray();

    //        foreach (var row in query.Transaction.Provider.GetTableCache(query.Table).GetRows(keys, query.Transaction))
    //            yield return (T)row;
    //    }
    //    else
    //    {
    //        var rows = this
    //            .ReadRows()
    //            .Select(x => InstanceFactory.NewImmutableRow(x, query.Transaction));

    //        foreach (var row in rows)
    //            yield return (T)row;
    //    }
    //}

    public IEnumerable<V> ExecuteAs<V>() =>
        Execute().Select(x => (V)x);

    public IEnumerable<object> Execute()
    {
        if (query.Table.PrimaryKeyColumns.Length != 0)
        {
            this.What(query.Table.PrimaryKeyColumns);

            var keys = this
                .ReadKeys()
                .ToArray();

            foreach (var row in query.Transaction.Provider.GetTableCache(query.Table).GetRows(keys, query.Transaction, orderings: query.OrderByList))
                yield return row;
        }
        else
        {
            var rows = this
                .ReadRows()
                .Select(x => InstanceFactory.NewImmutableRow(x, query.Transaction.Provider, query.Transaction));

            foreach (var row in rows)
                yield return row;
        }
    }

    public override string ToString()
    {
        return ToSql().ToString();
    }
}