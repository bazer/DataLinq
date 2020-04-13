using Slim.Extensions;
using Slim.Instances;
using Slim.Interfaces;
using Slim.Metadata;
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

        public Transaction Insert(IModel model)
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            if (!model.IsNew())
                throw new ArgumentException("Model is not a new row, unable to insert");

            AddAndExecute(model, TransactionChangeType.Insert);

            return this;
        }

        public Transaction Update(IModel model)
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            AddAndExecute(model, TransactionChangeType.Update);

            return this;
        }

        public Transaction Delete(IModel model)
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            AddAndExecute(model, TransactionChangeType.Delete);

            return this;
        }

        private void AddAndExecute(IModel model, TransactionChangeType type)
        {
            var table = Provider.Metadata.Tables.Single(x => x.Model.CsType == model.GetType() || x.Model.ProxyType == model.GetType() || x.Model.MutableProxyType == model.GetType());

            AddAndExecute(new StateChange(model, table, type));
        }

        private void AddAndExecute(params StateChange[] changes)
        {
            Changes.AddRange(changes);

            var commands = changes.Select(x => Provider.ToDbCommand(GetQuery(x)));

            foreach (var command in commands)
                DbTransaction.ExecuteNonQuery(command);
        }

        public void Commit()
        {
            CheckIfTransactionIsValid();

            DbTransaction.Commit();

            Provider.State.ApplyChanges(Changes.ToArray());
        }

        public void Rollback()
        {
            CheckIfTransactionIsValid();

            DbTransaction.Rollback();
        }

        private IQuery GetQuery(StateChange change)
        {
            var query = new SqlQuery(change.Table, this);

            if (change.Type == TransactionChangeType.Insert)
            {
                foreach (var column in change.Table.Columns)
                    query.Set(column.DbName, column.ValueProperty.GetValue(change.Model));

                return query.InsertQuery();
            }
            else if (change.Type == TransactionChangeType.Update)
            {
                foreach (var key in change.Table.PrimaryKeyColumns)
                    query.Where(key.DbName).EqualTo(key.ValueProperty.GetValue(change.Model));

                foreach (var column in change.Table.Columns)
                    query.Set(column.DbName, column.ValueProperty.GetValue(change.Model));

                return query.UpdateQuery();
            }
            else if (change.Type == TransactionChangeType.Delete)
            {
                foreach (var key in change.Table.PrimaryKeyColumns)
                    query.Where(key.DbName).EqualTo(key.ValueProperty.GetValue(change.Model));

                return query.DeleteQuery();
            }

            throw new NotImplementedException();
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

        public T From() => Schema;

        public SqlQuery From(string tableName)
        {
            var table = Provider.Metadata.Tables.Single(x => x.DbName == tableName);

            return new SqlQuery(table, this);
        }

        public SqlQuery From(Table table)
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