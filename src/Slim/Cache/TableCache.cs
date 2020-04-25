using Slim.Extensions;
using Slim.Instances;
using Slim.Metadata;
using Slim.Mutation;
using Slim.Query;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Slim.Cache
{
    public class TableCache
    {
        protected ConcurrentDictionary<PrimaryKeys, object> Rows = new ConcurrentDictionary<PrimaryKeys, object>();
        protected int primaryKeyColumnsCount;

        public TableCache(Table table)
        {
            this.Table = table;
            this.Table.Cache = this;
            this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Count;
        }

        public int RowCount => Rows.Count;
        public Table Table { get; }

        public void Apply(params StateChange[] changes)
        {
            foreach (var change in changes)
            {
                if (change.Table != Table)
                    continue;

                if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
                {
                    Rows.TryRemove(change.PrimaryKeys, out var temp);
                }
            }
        }

        public IEnumerable<PrimaryKeys> GetKeys(ForeignKey foreignKey, Transaction transaction)
        {
            var select = new SqlQuery(Table, transaction)
                .What(Table.PrimaryKeyColumns)
                .Where(foreignKey.Column.DbName).EqualTo(foreignKey.Data)
                .SelectQuery();

            return select
                .ReadKeys();
        }

        public IEnumerable<RowData> GetRowDataFromPrimaryKeys(IEnumerable<PrimaryKeys> keys, Transaction transaction, List<OrderBy> orderings = null)
        {
            var q = new SqlQuery(Table.DbName, transaction);

            foreach (var key in keys)
            {
                var where = q.CreateWhereGroup(BooleanType.Or);
                for (var i = 0; i < primaryKeyColumnsCount; i++)
                    where.And(Table.PrimaryKeyColumns[i].DbName).EqualTo(key.Data[i]);
            }

            if (orderings != null)
            {
                foreach (var order in orderings)
                    q.OrderBy(order.Column, order.Ascending);
            }

            return q
                .SelectQuery()
                .ReadRows();
        }

        public IEnumerable<RowData> GetRowDataFromForeignKey(ForeignKey foreignKey, Transaction transaction)
        {
            var select = new SqlQuery(Table, transaction)
                .Where(foreignKey.Column.DbName).EqualTo(foreignKey.Data)
                .SelectQuery();

            return select.ReadRows();
        }

        public IEnumerable<object> GetRows(ForeignKey foreignKey, Transaction transaction, List<OrderBy> orderings = null)
        {
            var keys = foreignKey.Column.Index.GetOrAdd(foreignKey.Data, _ => GetKeys(foreignKey, transaction).ToArray());

            return GetRows(keys, transaction, foreignKey, orderings);
        }

        public IEnumerable<object> GetRows(PrimaryKeys[] primaryKeys, Transaction transaction, ForeignKey foreignKey = null, List<OrderBy> orderings = null)
        {
            var keysToLoad = new List<PrimaryKeys>(primaryKeys.Length);
            foreach (var key in primaryKeys)
            {
                if (transaction.Type == TransactionType.NoTransaction && Rows.TryGetValue(key, out object row))
                    yield return row;
                else
                    keysToLoad.Add(key);
            }

            if (foreignKey != null && keysToLoad.Count > primaryKeys.Length / 2)
            {
                foreach (var rowData in GetRowDataFromForeignKey(foreignKey, transaction))
                {
                    var row = InstanceFactory.NewImmutableRow(rowData);

                    if (transaction.Type == TransactionType.NoTransaction)
                    {
                        if (Rows.TryAdd(rowData.GetKeys(), row))
                            yield return row;
                    }
                    else
                    {
                        yield return row;
                    }
                }
            }
            else if (keysToLoad.Count != 0)
            {
                foreach (var split in keysToLoad.SplitList(100))
                {
                    foreach (var rowData in GetRowDataFromPrimaryKeys(split, transaction, orderings))
                    {
                        var row = InstanceFactory.NewImmutableRow(rowData);

                        if (transaction.Type == TransactionType.NoTransaction)
                        {
                            if (!Rows.TryAdd(rowData.GetKeys(), row))
                                throw new Exception("Couldn't add row");
                        }

                        yield return row;
                    }
                }
            }
        }
    }
}