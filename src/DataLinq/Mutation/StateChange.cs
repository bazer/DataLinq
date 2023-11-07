using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace DataLinq.Mutation
{

    public class StateChange
    {
        public TransactionChangeType Type { get; }
        public IModel Model { get; }
        public TableMetadata Table { get; }

        public PrimaryKeys PrimaryKeys { get; }
        public bool HasAutoIncrement =>
            Table.PrimaryKeyColumns.Any(x => x.AutoIncrement);


        public StateChange(IModel model, TableMetadata table, TransactionChangeType type)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (table == null)
                throw new ArgumentNullException(nameof(table));

            Model = model;
            Table = table;
            Type = type;

            PrimaryKeys = new PrimaryKeys(Table.PrimaryKeyColumns.Select(x => x.ValueProperty.GetValue(Model)));
        }

        public void ExecuteQuery(Transaction transaction)
        {
            if (Type == TransactionChangeType.Insert && HasAutoIncrement && Table.PrimaryKeyColumns.Select(x => x.ValueProperty.GetValue(Model)).All(x => x == default))
            {
                var newId = transaction.DatabaseTransaction.ExecuteScalar(GetDbCommand(transaction));

                Table.PrimaryKeyColumns
                    .FirstOrDefault(x => x.AutoIncrement)?
                    .ValueProperty
                    .SetValue(Model, newId);
            }
            else
                transaction.DatabaseTransaction.ExecuteNonQuery(GetDbCommand(transaction));
        }

        public IDbCommand GetDbCommand(Transaction transaction) =>
            transaction.Provider.ToDbCommand(GetQuery(transaction));

        public IQuery GetQuery(Transaction transaction)
        {
            var query = new SqlQuery(Table, transaction);
            var writer = transaction.Provider.GetWriter();

            if (Type == TransactionChangeType.Insert)
            {
                foreach (var column in Table.Columns)
                {
                    var val = writer.ConvertColumnValue(column, column.ValueProperty.GetValue(Model));

                    //if (column.ValueProperty.CsType.BaseType == typeof(Enum) && column.ValueProperty.Column.DbTypes.Any(x => x.Name == "enum") && (int)val == 0)
                    //    val = null;

                    query.Set(column.DbName, val);
                }

                if (HasAutoIncrement)
                    query.AddLastIdQuery();

                return query.InsertQuery();
            }
            else if (Type == TransactionChangeType.Update)
            {
                foreach (var column in Table.PrimaryKeyColumns)
                    query.Where(column.DbName).EqualTo(writer.ConvertColumnValue(column, column.ValueProperty.GetValue(Model)));

                foreach (var change in (Model as MutableInstanceBase).GetChanges())
                    query.Set(change.Key.DbName, writer.ConvertColumnValue(change.Key, change.Value));

                return query.UpdateQuery();
            }
            else if (Type == TransactionChangeType.Delete)
            {
                foreach (var column in Table.PrimaryKeyColumns)
                    query.Where(column.DbName).EqualTo(writer.ConvertColumnValue(column, column.ValueProperty.GetValue(Model)));

                return query.DeleteQuery();
            }

            throw new NotImplementedException();
        }

        
    }
}