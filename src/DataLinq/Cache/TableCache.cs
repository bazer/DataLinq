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
        protected ConcurrentQueue<(PrimaryKeys keys, long ticks, int size)> KeysTicks = new();
        protected ConcurrentDictionary<PrimaryKeys, object> Rows = new();
        protected ConcurrentDictionary<Transaction, ConcurrentDictionary<PrimaryKeys, object>> TransactionRows = new();
        protected int primaryKeyColumnsCount;

        public TableCache(TableMetadata table)
        {
            this.Table = table;
            this.Table.Cache = this;
            this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Count;
        }

        public int RowCount => Rows.Count;
        public int KeysTicksCount => KeysTicks.Count;
        public int TransactionRowsCount => TransactionRows.Count;
        public long TotalBytes => KeysTicks.Sum(x => x.size);
        public TableMetadata Table { get; }

        public void Apply(params StateChange[] changes)
        {
            foreach (var change in changes)
            {
                if (change.Table != Table)
                    continue;

                if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
                {
                    Rows.TryRemove(change.PrimaryKeys, out var _);
                }
            }
        }

        public void ClearRows()
        {
            Rows.Clear();
            KeysTicks.Clear();
        }

        public int RemoveRowsByLimit(CacheLimitType limitType, long amount)
        {
            if (limitType == CacheLimitType.Seconds)
                return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromSeconds(amount)).Ticks);

            if (limitType == CacheLimitType.Minutes)
                return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromMinutes(amount)).Ticks);

            if (limitType == CacheLimitType.Hours)
                return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromHours(amount)).Ticks);

            if (limitType == CacheLimitType.Days)
                return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromDays(amount)).Ticks);

            if (limitType == CacheLimitType.Ticks)
                return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromTicks(amount)).Ticks);

            if (limitType == CacheLimitType.Rows)
                return RemoveRowsOverRowLimit((int)amount);

            if (limitType == CacheLimitType.Bytes)
                return RemoveRowsOverSizeLimit(amount);

            if (limitType == CacheLimitType.Kilobytes)
                return RemoveRowsOverSizeLimit(amount * 1024);

            if (limitType == CacheLimitType.Megabytes)
                return RemoveRowsOverSizeLimit(amount * 1024 * 1024);

            if (limitType == CacheLimitType.Gigabytes)
                return RemoveRowsOverSizeLimit(amount * 1024 * 1024 * 1024);

            throw new NotImplementedException();
        }

        public int RemoveRowsOverRowLimit(int maxRows)
        {
            var count = 0;
            var rowCount = RowCount;

            KeysTicks.TryPeek(out var row);

            while (rowCount > maxRows)
            {
                if (TryRemoveRow(row.keys, out var numRowsRemoved))
                {
                    rowCount -= numRowsRemoved;
                    count += numRowsRemoved;

                    KeysTicks.TryDequeue(out row);
                }
                else
                    break;
            }

            return count;
        }

        public int RemoveRowsOverSizeLimit(long maxSize)
        {
            var count = 0;
            var totalSize = TotalBytes;

            KeysTicks.TryPeek(out var row);

            while (totalSize > maxSize)
            {
                if (TryRemoveRow(row.keys, out var numRowsRemoved))
                {
                    totalSize -= row.size;
                    count += numRowsRemoved;

                    KeysTicks.TryDequeue(out row);
                }
                else
                    break;
            }

            return count;
        }

        public int RemoveRowsInsertedBeforeTick(long tick)
        {
            if (!KeysTicks.TryPeek(out var row))
                return 0;

            var count = 0;

            while (row.ticks < tick)
            {
                if (TryRemoveRow(row.keys, out var numRowsRemoved))
                {
                    count += numRowsRemoved;

                    KeysTicks.TryDequeue(out var _);
                }
                else
                    break;

                if (!KeysTicks.TryPeek(out row))
                    break;
            }

            return count;
        }

        private bool TryRemoveRow(PrimaryKeys primaryKeys, out int numRowsRemoved)
        {
            numRowsRemoved = 0;

            if (Rows.ContainsKey(primaryKeys))
            {
                if (Rows.TryRemove(primaryKeys, out var _))
                {
                    numRowsRemoved = 1;
                    return true;
                }
                else
                    return false;
            }
                    

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
                if (transaction.Type == TransactionType.NoTransaction && Rows.TryGetValue(key, out var row))
                    yield return row;
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


            if ((transaction.Type == TransactionType.NoTransaction && (!Table.UseCache || TryAddRowAllDict(keys, rowData, row)))
                || (transaction.Type != TransactionType.NoTransaction && TransactionRows.TryGetValue(transaction, out var transactionRows) && transactionRows.TryAdd(keys, row)))
            {
                return true;
            }

            return false;

            bool TryAddRowAllDict(PrimaryKeys keys, RowData data, object instance)
            {
                var ticks = DateTime.Now.Ticks;

                if (!Rows.TryAdd(keys, instance))
                    return false;

                KeysTicks.Enqueue((keys, ticks, data.Size));

                return true;
            }
        }


    }
}