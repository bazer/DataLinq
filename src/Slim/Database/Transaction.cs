using Slim.Instances;
using Slim.Interfaces;
using Slim.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Slim
{
    public enum TransactionType
    {
        NoTransaction,
        ReadAndWrite,
        ReadOnly,
        WriteOnly
    }

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

    public class Transaction : IDisposable
    {
        public IDatabaseProvider DatabaseProvider { get; }

        public DatabaseTransaction DatabaseTransaction { get; set; }
        public List<TransactionChange> Changes { get; } = new List<TransactionChange>();

        public TransactionType Type { get; protected set; }

        public Transaction(IDatabaseProvider databaseProvider, TransactionType type)
        {
            this.DatabaseProvider = databaseProvider;
            this.DatabaseTransaction = databaseProvider.GetNewDatabaseTransaction(type);
            this.Type = type;
        }

        public void Insert(IModel model)
        {
            AddAndExecute(new TransactionChange(model, TransactionChangeType.Insert));
        }

        public void Update(IModel model)
        {
            AddAndExecute(new TransactionChange(model, TransactionChangeType.Update));
        }

        public void Delete(IModel model)
        {
            AddAndExecute(new TransactionChange(model, TransactionChangeType.Delete));
        }

        private void AddAndExecute(params TransactionChange[] changes)
        {
            Changes.AddRange(changes);

            var commands = changes.Select(x => DatabaseProvider.ToDbCommand(GetQuery(x)));

            foreach (var command in commands)
                DatabaseTransaction.ExecuteNonQuery(command);
        }

        public void Commit()
        {
            DatabaseTransaction.Commit();


        }

        public void Rollback()
        {
            DatabaseTransaction.Rollback();
        }

        //private Table GetTable() =>
        //    DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

        private IQuery GetQuery(TransactionChange change)
        {
            var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

            if (change.Type == TransactionChangeType.Insert)
            {
                var insert = new Insert(this, table);

                foreach (var column in table.Columns)
                    insert.With(column.DbName, column.ValueProperty.GetValue(change.Model));

                return insert;
            }
            else if (change.Type == TransactionChangeType.Delete)
            {
                var delete = new Delete(this, table);

                foreach (var key in table.PrimaryKeyColumns)
                    delete.Where(key.DbName).EqualTo(key.ValueProperty.GetValue(change.Model));

                return delete;
            }

            throw new NotImplementedException();
        }

        public void Dispose()
        {
            DatabaseTransaction.Dispose();
        }
    }

    public class Transaction<T> : Transaction where T : class, IDatabaseModel
    {
        public T Read() => Schema;

        public T Schema { get; }

        public Transaction(DatabaseProvider<T> databaseProvider, TransactionType type) : base(databaseProvider, type)
        {
            this.Schema = GetDatabaseInstance();
        }

        private T GetDatabaseInstance()
        {
            return InstanceFactory.NewDatabase<T>(this);
        }
    }
}