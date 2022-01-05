using DataLinq.Extensions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Mutation
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
        public IDatabaseProvider Provider { get; }

        public DatabaseTransaction DbTransaction { get; set; }
        public List<StateChange> Changes { get; } = new List<StateChange>();

        public TransactionType Type { get; protected set; }

        public DatabaseTransactionStatus Status => DbTransaction.Status;

        public Transaction(IDatabaseProvider databaseProvider, TransactionType type)
        {
            Provider = databaseProvider;
            DbTransaction = databaseProvider.GetNewDatabaseTransaction(type);
            Type = type;
        }

        public T Insert<T>(T model) where T: IModel
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            if (!model.IsNewModel())
                throw new ArgumentException("Model is not a new row, unable to insert");

            AddAndExecute(model, TransactionChangeType.Insert);

            return GetModelFromCache(model);
        }

        public T Update<T>(T model) where T : IModel
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            AddAndExecute(model, TransactionChangeType.Update);

            return GetModelFromCache(model);
        }

        public T Update<T>(T model, Action<T> changes) where T : IModel
        {
            var mut = model.Mutate();
            changes(mut);

            return Update(mut);
        }

        public T Save<T>(T model) where T : IModel
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            if (model.IsNewModel())
                AddAndExecute(model, TransactionChangeType.Insert);
            else
                AddAndExecute(model, TransactionChangeType.Update);

            return GetModelFromCache(model);
        }

        public void Delete(IModel model)
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            AddAndExecute(model, TransactionChangeType.Delete);
        }

        private void AddAndExecute(IModel model, TransactionChangeType type)
        {
            var table = Provider.Metadata.Tables.Single(x => x.Model.IsOfType(model.GetType()));

            AddAndExecute(new StateChange(model, table, type));
        }

        private void AddAndExecute(params StateChange[] changes)
        {
            Changes.AddRange(changes);

            foreach (var change in changes)
                change.ExecuteQuery(this);
        }

        public void Commit()
        {
            CheckIfTransactionIsValid();

            DbTransaction.Commit();

            Provider.State.ApplyChanges(Changes.ToArray());
            Provider.State.RemoveTransactionFromCache(this);
        }

        public void Rollback()
        {
            CheckIfTransactionIsValid();

            DbTransaction.Rollback();
            Provider.State.RemoveTransactionFromCache(this);
        }

        private T GetModelFromCache<T>(T model) where T : IModel
        {
            var metadata = Model.Find(model);
            var keys = model.PrimaryKeys();

           return (T)metadata.Table.Cache.GetRow(keys, this);
        }


        private void CheckIfTransactionIsValid()
        {
            if (Type == TransactionType.NoTransaction)
                return;

            if (Status == DatabaseTransactionStatus.Committed)
                throw new Exception("Transaction is already committed");

            if (Status == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Transaction is rolled back");
        }

        public void Dispose()
        {
            Provider.State.RemoveTransactionFromCache(this);
            DbTransaction.Dispose();
        }
    }

    public class Transaction<T> : Transaction where T : class, IDatabaseModel
    {
        protected T Schema { get; }

        public Transaction(DatabaseProvider<T> databaseProvider, TransactionType type) : base(databaseProvider, type)
        {
            Schema = GetDatabaseInstance();
        }

        public T Query() => Schema;

        public SqlQuery From(string tableName)
        {
            var table = Provider.Metadata.Tables.Single(x => x.DbName == tableName);

            return new SqlQuery(table, this);
        }

        public SqlQuery From(TableMetadata table)
        {
            return new SqlQuery(table, this);
        }

        public SqlQuery<V> From<V>() where V : IModel
        {
            return new SqlQuery<V>(this);
        }

        private T GetDatabaseInstance()
        {
            return InstanceFactory.NewDatabase<T>(this);
        }
    }
}