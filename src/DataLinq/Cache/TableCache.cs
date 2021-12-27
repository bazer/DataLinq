using DataLinq.Extensions;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataLinq.Cache
{
    public class TableCache
    {
        protected ConcurrentDictionary<PrimaryKeys, (object value, long ticks)> Rows = new ConcurrentDictionary<PrimaryKeys, (object, long)>();
        protected ConcurrentDictionary<Transaction, ConcurrentDictionary<PrimaryKeys, object>> TransactionRows = new ConcurrentDictionary<Transaction, ConcurrentDictionary<PrimaryKeys, object>>();
        protected int primaryKeyColumnsCount;

        public TableCache(Table table)
        {
            this.Table = table;
            this.Table.Cache = this;
            this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Count;
        }

        public int RowCount => Rows.Count;
        public int TransactionRowsCount => TransactionRows.Count;
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

        public void ClearRows()
        {
            Rows.Clear();
        }

        public int RemoveRowsInsertedBeforeTick(long tick)
        {
            var count = 0;
            var rows = GetRowsInsertedBeforeTick(tick, 1000).ToList();

            while (rows.Count > 0)
            {
                foreach (var row in rows)
                {
                    if (TryRemoveRow(row.Key))
                        count += 1;
                }

                rows = GetRowsInsertedBeforeTick(tick, 1000).ToList();
            }

            return count;
        }

        private IEnumerable<KeyValuePair<PrimaryKeys, (object value, long ticks)>> GetRowsInsertedBeforeTick(long tick, int take)
        {
            return Rows
                .Where(x => x.Value.ticks < tick)
                .Take(take);
        }

        public bool TryRemoveRow(PrimaryKeys primaryKeys)
        {
            if (Rows.ContainsKey(primaryKeys))
                return Rows.TryRemove(primaryKeys, out var _);

            return true;
        }

        public bool TryRemoveTransaction(Transaction transaction)
        {
            if (TransactionRows.ContainsKey(transaction))
                return TransactionRows.TryRemove(transaction, out var _);

            return true;
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
                    q.OrderBy(order.Column, order.Alias, order.Ascending);
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
            if (foreignKey.Data == null)
                return new List<object>();

            var keys = foreignKey.Column.Index.GetOrAdd(foreignKey.Data, _ => GetKeys(foreignKey, transaction).ToArray());

            return GetRows(keys, transaction, foreignKey, orderings);
        }

        public object GetRow(PrimaryKeys primaryKeys, Transaction transaction) =>
            GetRows(primaryKeys.Yield().ToArray(), transaction).FirstOrDefault();

        public IEnumerable<object> GetRows(PrimaryKeys[] primaryKeys, Transaction transaction, ForeignKey foreignKey = null, List<OrderBy> orderings = null)
        {
            if (transaction.Type != TransactionType.NoTransaction && !TransactionRows.ContainsKey(transaction))
                TransactionRows.TryAdd(transaction, new ConcurrentDictionary<PrimaryKeys, object>());

            var keysToLoad = new List<PrimaryKeys>(primaryKeys.Length);
            foreach (var key in primaryKeys)
            {
                if (transaction.Type == TransactionType.NoTransaction && Rows.TryGetValue(key, out (object value, long) row))
                    yield return row.value;
                else if (transaction.Type != TransactionType.NoTransaction && TransactionRows.TryGetValue(transaction, out var transactionRows) && transactionRows.TryGetValue(key, out object transactionRow))
                    yield return transactionRow;
                else
                    keysToLoad.Add(key);
            }

            if (foreignKey != null && keysToLoad.Count > primaryKeys.Length / 2)
            {
                foreach (var rowData in GetRowDataFromForeignKey(foreignKey, transaction))
                {
                    if (TryAddRow(rowData, transaction, out var row))
                        yield return row;
                }
            }
            else if (keysToLoad.Count != 0)
            {
                foreach (var split in keysToLoad.SplitList(100))
                {
                    foreach (var rowData in GetRowDataFromPrimaryKeys(split, transaction, orderings))
                    {
                        yield return AddRow(rowData, transaction);
                    }
                }
            }
        }

        private object AddRow(RowData rowData, Transaction transaction)
        {
            TryAddRow(rowData, transaction, out var row);
            return row;
        }

        private bool TryAddRow(RowData rowData, Transaction transaction, out object row)
        {
            row = InstanceFactory.NewImmutableRow(rowData);
            var keys = rowData.GetKeys();
            var ticks = DateTime.Now.Ticks;

            if ((transaction.Type == TransactionType.NoTransaction && Rows.TryAdd(keys, (row, ticks)))
                || (transaction.Type != TransactionType.NoTransaction && TransactionRows.TryGetValue(transaction, out var transactionRows) && transactionRows.TryAdd(keys, row)))
            {
                return true;
            }

            return false;
        }
    }
}