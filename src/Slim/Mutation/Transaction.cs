using Slim.Instances;
using Slim.Interfaces;
using Slim.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Slim.Mutation
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

    public class Transaction : IDisposable
    {
        public IDatabaseProvider DatabaseProvider { get; }

        public DatabaseTransaction DatabaseTransaction { get; set; }
        public List<StateChange> Changes { get; } = new List<StateChange>();

        public TransactionType Type { get; protected set; }

        public Transaction(IDatabaseProvider databaseProvider, TransactionType type)
        {
            DatabaseProvider = databaseProvider;
            DatabaseTransaction = databaseProvider.GetNewDatabaseTransaction(type);
            Type = type;
        }

        public void Insert(IModel model)
        {
            AddAndExecute(model, TransactionChangeType.Insert);
        }

        public void Update(IModel model)
        {
            AddAndExecute(model, TransactionChangeType.Update);
        }

        public void Delete(IModel model)
        {
            AddAndExecute(model, TransactionChangeType.Delete);
        }

        private void AddAndExecute(IModel model, TransactionChangeType type)
        {
            var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == model.GetType() || x.Model.ProxyType == model.GetType());

            AddAndExecute(new StateChange(model, table, type));
        }

        private void AddAndExecute(params StateChange[] changes)
        {
            Changes.AddRange(changes);

            var commands = changes.Select(x => DatabaseProvider.ToDbCommand(GetQuery(x)));

            foreach (var command in commands)
                DatabaseTransaction.ExecuteNonQuery(command);
        }

        public void Commit()
        {
            DatabaseTransaction.Commit();

            DatabaseProvider.State.ApplyChanges(Changes.ToArray());
        }

        public void Rollback()
        {
            DatabaseTransaction.Rollback();
        }

        //private Table GetTable() =>
        //    DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

        private IQuery GetQuery(StateChange change)
        {
            //var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

            if (change.Type == TransactionChangeType.Insert)
            {
                var insert = new Insert(this, change.Table);

                foreach (var column in change.Table.Columns)
                    insert.With(column.DbName, column.ValueProperty.GetValue(change.Model));

                return insert;
            }
            else if (change.Type == TransactionChangeType.Delete)
            {
                var delete = new Delete(this, change.Table);

                foreach (var key in change.Table.PrimaryKeyColumns)
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
            Schema = GetDatabaseInstance();
        }

        private T GetDatabaseInstance()
        {
            return InstanceFactory.NewDatabase<T>(this);
        }
    }
}