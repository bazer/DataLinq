using Slim.Extensions;
using Slim.Instances;
using Slim.Metadata;
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
            this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Count;
        }

        public int RowCount => Rows.Count;
        public Table Table { get; }

        public IEnumerable<PrimaryKeys> GetKeys(ForeignKey foreignKey, Transaction transaction)
        {
            var select = new Select(transaction, Table)
                .What(Table.PrimaryKeyColumns)
                .Where(foreignKey.Column.DbName).EqualTo(foreignKey.Data);

            return select
                .ReadKeys();
        }

        public IEnumerable<RowData> GetRowDataFromPrimaryKeys(IEnumerable<PrimaryKeys> keys, Transaction transaction)
        {
            var select = new Select(transaction, Table);

            var query = new StringBuilder()
                .Append("SELECT * FROM ")
                .Append(Table.DbName)
                .Append(" WHERE ")
                .Append(keys
                    .Select(x => $"({formatRowKey(x.Data).ToJoinedString(" AND ")})")
                    .ToJoinedString(" OR "));

            return transaction.DatabaseTransaction.ReadReader(query.ToString())
                .Select(x => new RowData(x, Table));

            IEnumerable<string> formatRowKey(object[] data)
            {
                for (var i = 0; i < primaryKeyColumnsCount; i++)
                    yield return $"{Table.PrimaryKeyColumns[i].DbName} = '{data[i]}'";
            }
        }

        public IEnumerable<RowData> GetRowDataFromForeignKey(ForeignKey foreignKey, Transaction transaction)
        {
            var select = new Select(transaction, Table);
            select.Where(foreignKey.Column.DbName).EqualTo(foreignKey.Data);

            return select.ReadInstances();
        }

        public IEnumerable<object> GetRows(ForeignKey foreignKey, Transaction transaction)
        {
            var keys = foreignKey.Column.Index.GetOrAdd(foreignKey.Data, _ => GetKeys(foreignKey, transaction).ToArray());

            return GetRows(keys, transaction, foreignKey);
        }

        public IEnumerable<object> GetRows(PrimaryKeys[] primaryKeys, Transaction transaction, ForeignKey foreignKey = null)
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
                        if (Rows.TryAdd(rowData.GetKey(), row))
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
                    foreach (var rowData in GetRowDataFromPrimaryKeys(split, transaction))
                    {
                        var row = InstanceFactory.NewImmutableRow(rowData);

                        if (transaction.Type == TransactionType.NoTransaction)
                        {
                            if (!Rows.TryAdd(rowData.GetKey(), row))
                                throw new Exception("Couldn't add row");
                        }

                        yield return row;
                    }
                }
            }
        }
    }
}