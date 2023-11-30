using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Mutation
{
    /// <summary>
    /// Enumerates the types of transactions.
    /// </summary>
    public enum TransactionType
    {
        /// <summary>
        /// Transaction that allows both read and write operations.
        /// </summary>
        ReadAndWrite,
        /// <summary>
        /// Transaction that only allows read operations.
        /// </summary>
        ReadOnly,
        /// <summary>
        /// Transaction that only allows write operations.
        /// </summary>
        WriteOnly
    }

    /// <summary>
    /// Enumerates the types of changes that can be made to a transaction.
    /// </summary>
    public enum TransactionChangeType
    {
        /// <summary>
        /// Insert a new row into the database.
        /// </summary>
        Insert,
        /// <summary>
        /// Update an existing row in the database.
        /// </summary>
        Update,
        /// <summary>
        /// Delete an existing row from the database.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Provides data for the <see cref="Transaction.OnStatusChanged"/> event.
    /// </summary>
    public class TransactionStatusChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TransactionStatusChangeEventArgs"/> class.
        /// </summary>
        /// <param name="transaction">The transaction that raised the event.</param>
        /// <param name="status">The new status of the transaction.</param>
        public TransactionStatusChangeEventArgs(Transaction transaction, DatabaseTransactionStatus status)
        {
            Transaction = transaction;
            Status = status;
        }

        /// <summary>
        /// Gets the transaction that raised the event.
        /// </summary>
        public Transaction Transaction { get; }

        /// <summary>
        /// Gets the new status of the transaction.
        /// </summary>
        public DatabaseTransactionStatus Status { get; }
    }

    /// <summary>
    /// Represents a database transaction.
    /// </summary>
    public class Transaction : IDisposable, IEquatable<Transaction>
    {
        private static uint transactionCount = 0;

        /// <summary>
        /// Gets the ID of the transaction.
        /// </summary>
        public uint TransactionID { get; }

        /// <summary>
        /// Gets the database provider.
        /// </summary>
        public IDatabaseProvider Provider { get; }

        /// <summary>
        /// Gets or sets the database transaction.
        /// </summary>
        public DatabaseTransaction DatabaseTransaction { get; set; }

        /// <summary>
        /// Gets the list of state changes.
        /// </summary>
        public List<StateChange> Changes { get; } = new List<StateChange>();

        /// <summary>
        /// Gets the type of the transaction.
        /// </summary>
        public TransactionType Type { get; protected set; }

        /// <summary>
        /// Gets the status of the database transaction.
        /// </summary>
        public DatabaseTransactionStatus Status => DatabaseTransaction.Status;

        /// <summary>
        /// Occurs when the status of the transaction changes.
        /// </summary>
        public event EventHandler<TransactionStatusChangeEventArgs>? OnStatusChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="Transaction"/> class.
        /// </summary>
        /// <param name="databaseProvider">The database provider.</param>
        /// <param name="type">The type of the transaction.</param>
        public Transaction(IDatabaseProvider databaseProvider, TransactionType type)
        {
            Provider = databaseProvider;
            DatabaseTransaction = databaseProvider.GetNewDatabaseTransaction(type);
            DatabaseTransaction.OnStatusChanged += (_, args) => OnStatusChanged?.Invoke(this, new TransactionStatusChangeEventArgs(this, args.Status));
            Type = type;

            TransactionID = Interlocked.Increment(ref transactionCount);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Transaction"/> class.
        /// </summary>
        /// <param name="databaseProvider">The database provider.</param>
        /// <param name="dbTransaction">The database transaction.</param>
        /// <param name="type">The type of the transaction.</param>
        public Transaction(IDatabaseProvider databaseProvider, IDbTransaction dbTransaction, TransactionType type)
        {
            Provider = databaseProvider;
            DatabaseTransaction = databaseProvider.AttachDatabaseTransaction(dbTransaction, type);
            DatabaseTransaction.OnStatusChanged += (_, args) => OnStatusChanged?.Invoke(this, new TransactionStatusChangeEventArgs(this, args.Status));
            Type = type;

            TransactionID = Interlocked.Increment(ref transactionCount);
        }

        /// <summary>
        /// Inserts a new row into the database.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="model">The model to insert.</param>
        /// <returns>The inserted model.</returns>
        public T Insert<T>(T model) where T : IModel
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            if (!model.IsNewModel())
                throw new ArgumentException("Model is not a new row, unable to insert");

            AddAndExecute(model, TransactionChangeType.Insert);

            return GetModelFromCache(model);
        }

        /// <summary>
        /// Inserts multiple new rows into the database.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="models">The models to insert.</param>
        /// <returns>The inserted models.</returns>
        public List<T> Insert<T>(IEnumerable<T> models) where T : IModel
        {
            return models
                .Select(Insert)
                .ToList();
        }

        /// <summary>
        /// Updates an existing row in the database.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="model">The model to update.</param>
        /// <returns>The updated model.</returns>
        public T Update<T>(T model) where T : IModel
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            if (model.IsNewModel())
                throw new ArgumentException("Model is a new row, unable to update");

            AddAndExecute(model, TransactionChangeType.Update);

            return GetModelFromCache(model);
        }

        /// <summary>
        /// Updates an existing row in the database with the specified changes.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="model">The model to update.</param>
        /// <param name="changes">The changes to apply to the model.</param>
        /// <returns>The updated model.</returns>
        public T Update<T>(T model, Action<T> changes) where T : IModel
        {
            var mut = model.Mutate();
            changes(mut);

            return Update(mut);
        }

        /// <summary>
        /// Inserts a new row into the database or updates an existing row if it already exists.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="model">The model to insert or update.</param>
        /// <returns>The inserted or updated model.</returns>
        public T InsertOrUpdate<T>(T model) where T : IModel
        {
            if (model == null)
                throw new ArgumentException("Model argument has null value");

            if (model.IsNewModel())
                return Insert(model);
            else
                return Update(model);
        }

        /// <summary>
        /// Inserts a new row into the database or updates an existing row if it already exists with the specified changes.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="model">The model to insert or update.</param>
        /// <param name="changes">The changes to apply to the model.</param>
        /// <returns>The inserted or updated model.</returns>
        public T InsertOrUpdate<T>(T model, Action<T> changes) where T : IModel, new()
        {
            if (model == null)
                model = new T();

            var mut = model.Mutate();
            changes(mut);

            return InsertOrUpdate(mut);
        }

        /// <summary>
        /// Deletes an existing row from the database.
        /// </summary>
        /// <param name="model">The model to delete.</param>
        public void Delete(IModel model)
        {
            CheckIfTransactionIsValid();

            if (model == null)
                throw new ArgumentException("Model argument has null value");

            AddAndExecute(model, TransactionChangeType.Delete);
        }

        /// <summary>
        /// Gets models from a query.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <returns>The models returned by the query.</returns>
        public IEnumerable<T> GetFromQuery<T>(string query) where T : IModel
        {
            var table = Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table;

            return DatabaseTransaction
                .ReadReader(query)
                .Select(x => new RowData(x, table))
                .Select(x => InstanceFactory.NewImmutableRow(x, Provider, this))
                .Cast<T>();
        }

        /// <summary>
        /// Gets models from a command.
        /// </summary>
        /// <typeparam name="T">The type of the model.</typeparam>
        /// <param name="dbCommand">The command to execute.</param>
        /// <returns>The models returned by the command.</returns>
        public IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand) where T : IModel
        {
            var table = Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table;

            return DatabaseTransaction
                .ReadReader(dbCommand)
                .Select(x => new RowData(x, table))
                .Select(x => InstanceFactory.NewImmutableRow(x, Provider, this))
                .Cast<T>();
        }

        private void AddAndExecute(IModel model, TransactionChangeType type)
        {
            var table = model.Metadata().Table;

            AddAndExecute(new StateChange(model, table, type));
        }

        private void AddAndExecute(params StateChange[] changes)
        {
            Changes.AddRange(changes);

            foreach (var change in changes)
                change.ExecuteQuery(this);

            Provider.State.ApplyChanges(changes, this);
        }

        /// <summary>
        /// Commits the transaction.
        /// </summary>
        public void Commit()
        {
            CheckIfTransactionIsValid();

            DatabaseTransaction.Commit();

            Provider.State.ApplyChanges(Changes);
            Provider.State.RemoveTransactionFromCache(this);
        }

        /// <summary>
        /// Rolls back the transaction.
        /// </summary>
        public void Rollback()
        {
            CheckIfTransactionIsValid();

            DatabaseTransaction.Rollback();
            Provider.State.RemoveTransactionFromCache(this);
        }

        private T GetModelFromCache<T>(T model) where T : IModel
        {
            var metadata = model.Metadata();
            var keys = model.PrimaryKeys(metadata);

            return (T)Provider.GetTableCache(metadata.Table).GetRow(keys, this);
        }

        private void CheckIfTransactionIsValid()
        {
            if (Type == TransactionType.ReadOnly)
                return;

            if (Status == DatabaseTransactionStatus.Committed)
                throw new Exception("Transaction is already committed");

            if (Status == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Transaction is rolled back");
        }

        /// <summary>
        /// Disposes of the transaction.
        /// </summary>
        public void Dispose()
        {
            Provider.State.RemoveTransactionFromCache(this);
            DatabaseTransaction.Dispose();
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="other">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public bool Equals(Transaction? other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return TransactionID.Equals(other.TransactionID);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != typeof(Transaction))
                return false;

            return TransactionID.Equals(((Transaction)obj).TransactionID);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            return TransactionID.GetHashCode();
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return $"Transaction with ID '{TransactionID}': {Type}";
        }
    }

    /// <summary>
    /// Represents a database transaction.
    /// </summary>
    /// <typeparam name="T">The type of the database model.</typeparam>
    public class Transaction<T> : Transaction where T : class, IDatabaseModel
    {
        /// <summary>
        /// Gets the database for the transaction.
        /// </summary>
        protected T Database { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Transaction{T}"/> class.
        /// </summary>
        /// <param name="databaseProvider">The database provider.</param>
        /// <param name="type">The type of the transaction.</param>
        public Transaction(DatabaseProvider<T> databaseProvider, TransactionType type) : base(databaseProvider, type)
        {
            Database = InstanceFactory.NewDatabase<T>(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Transaction{T}"/> class.
        /// </summary>
        /// <param name="databaseProvider">The database provider.</param>
        /// <param name="dbTransaction">The database transaction.</param>
        /// <param name="type">The type of the transaction.</param>
        public Transaction(DatabaseProvider<T> databaseProvider, IDbTransaction dbTransaction, TransactionType type) : base(databaseProvider, dbTransaction, type)
        {
            Database = InstanceFactory.NewDatabase<T>(this);
        }

        /// <summary>
        /// Gets the schema.
        /// </summary>
        /// <returns>The schema.</returns>
        public T Query() => Database;

        /// <summary>
        /// Creates a new SQL query from the specified table name.
        /// </summary>
        /// <param name="tableName">The name of the table.</param>
        /// <returns>The SQL query.</returns>
        public SqlQuery From(string tableName)
        {
            var table = Provider.Metadata.TableModels.Single(x => x.Table.DbName == tableName).Table;

            return new SqlQuery(table, this);
        }

        /// <summary>
        /// Creates a new SQL query from the specified table metadata.
        /// </summary>
        /// <param name="table">The table metadata.</param>
        /// <returns>The SQL query.</returns>
        public SqlQuery From(TableMetadata table)
        {
            return new SqlQuery(table, this);
        }

        /// <summary>
        /// Creates a new SQL query from the specified model type.
        /// </summary>
        /// <typeparam name="V">The type of the model.</typeparam>
        /// <returns>The SQL query.</returns>
        public SqlQuery<V> From<V>() where V : IModel
        {
            return new SqlQuery<V>(this);
        }
    }
}