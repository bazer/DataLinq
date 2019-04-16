using Modl.Db.Query;
using Slim.Extensions;
using Slim.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;

namespace Slim
{
    public enum TransactionChangeType
    {
        Insert,
        Update,
        Delete
    }

    public class TransactionChange
    {
        public TransactionChangeType Type { get; }
        public IModel Model { get; }

        public TransactionChange(IModel model, TransactionChangeType type)
        {
            Model = model;
            Type = type;
        }
    }

    public class Transaction<T> : IDisposable where T : class, IDatabaseModel
    {
        public T Read() => DatabaseProvider.Schema;
        public DatabaseProvider<T> DatabaseProvider { get; }

        private List<TransactionChange> changes = new List<TransactionChange>();

        public Transaction(DatabaseProvider<T> databaseProvider)
        {
            this.DatabaseProvider = databaseProvider;
        }

        public void Insert(IModel model)
        {
            changes.Add(new TransactionChange(model, TransactionChangeType.Insert));
        }

        public void Update(IModel model)
        {
            changes.Add(new TransactionChange(model, TransactionChangeType.Update));
        }

        public void Delete(IModel model)
        {
            changes.Add(new TransactionChange(model, TransactionChangeType.Delete));
        }

        public void Commit()
        {
            var changes = GetChanges();

            var commands = changes.Select(x => DatabaseProvider.ToDbCommand(x));

            foreach (var command in commands)
                DatabaseProvider.ExecuteNonQuery(command);
        }

        private IEnumerable<IQuery> GetChanges()
        {
            foreach (var change in changes)
            {
                //change.Model.RowData()

                var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

                if (change.Type == TransactionChangeType.Insert)
                {
                    var insert = new Insert(DatabaseProvider, table);

                    foreach (var column in table.Columns)
                        insert.With(column.DbName, column.ValueProperty.GetValue(change.Model));

                    yield return insert;
                }
                else if (change.Type == TransactionChangeType.Delete)
                {
                    var delete = new Delete(DatabaseProvider, table);

                    foreach (var key in table.PrimaryKeyColumns)
                        delete.Where(key.DbName).EqualTo(key.ValueProperty.GetValue(change.Model));

                    yield return delete;
                }

            }
        }

        public void Dispose()
        {
            
        }

    }
}
