﻿using Slim.Extensions;
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

        public DatabaseTransactionStatus TransactionStatus => DatabaseTransaction.Status;

        public Transaction(IDatabaseProvider databaseProvider, TransactionType type)
        {
            DatabaseProvider = databaseProvider;
            DatabaseTransaction = databaseProvider.GetNewDatabaseTransaction(type);
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
            var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == model.GetType() || x.Model.ProxyType == model.GetType() || x.Model.MutableProxyType == model.GetType());

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
            CheckIfTransactionIsValid();

            DatabaseTransaction.Commit();

            DatabaseProvider.State.ApplyChanges(Changes.ToArray());
        }

        public void Rollback()
        {
            CheckIfTransactionIsValid();

            DatabaseTransaction.Rollback();
        }

        //private Table GetTable() =>
        //    DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

        private IQuery GetQuery(StateChange change)
        {
            //var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == change.Model.GetType() || x.Model.ProxyType == change.Model.GetType());

            if (change.Type == TransactionChangeType.Insert)
            {
                var insert = new Insert(change.Table, this);

                foreach (var column in change.Table.Columns)
                    insert.With(column.DbName, column.ValueProperty.GetValue(change.Model));

                return insert;
            }
            else if (change.Type == TransactionChangeType.Update)
            {
                var update = new Update(change.Table, this);

                foreach (var key in change.Table.PrimaryKeyColumns)
                    update.Where(key.DbName).EqualTo(key.ValueProperty.GetValue(change.Model));

                foreach (var column in change.Table.Columns)
                    update.With(column.DbName, column.ValueProperty.GetValue(change.Model));

                return update;
            }
            else if (change.Type == TransactionChangeType.Delete)
            {
                var delete = new Delete(change.Table, this);

                foreach (var key in change.Table.PrimaryKeyColumns)
                    delete.Where(key.DbName).EqualTo(key.ValueProperty.GetValue(change.Model));

                return delete;
            }

            throw new NotImplementedException();
        }

        private void CheckIfTransactionIsValid()
        {
            if (Type == TransactionType.NoTransaction)
                return;

            if (TransactionStatus == DatabaseTransactionStatus.Committed)
                throw new Exception("Transaction is already committed");

            if (TransactionStatus == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Transaction is rolled back");
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