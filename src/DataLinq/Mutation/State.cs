using System;
using System.Collections.Generic;
using DataLinq.Cache;

namespace DataLinq.Mutation
{
    /// <summary>
    /// Represents the state of the database, including history and cache. It provides methods to apply changes to the state,
    /// manage the transactions in the cache, and handle cleanup of resources.
    /// </summary>
    public class State : IDisposable
    {
        /// <summary>
        /// Gets or sets the history of changes made to the database.
        /// </summary>
        public History History { get; set; }

        /// <summary>
        /// Gets or sets the cache associated with the database state.
        /// </summary>
        public DatabaseCache Cache { get; set; }

        /// <summary>
        /// Gets the database provider associated with the state.
        /// </summary>
        public DatabaseProvider Database { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="State"/> class with the specified database provider.
        /// </summary>
        /// <param name="database">The database provider to associate with the state.</param>
        public State(DatabaseProvider database)
        {
            this.Database = database;
            this.Cache = new DatabaseCache(database);
            this.History = new History();
        }

        /// <summary>
        /// Applies a collection of state changes to the database using an optional transaction context.
        /// </summary>
        /// <param name="changes">The state changes to apply.</param>
        /// <param name="transaction">The transaction to associate with the changes, if any.</param>
        public void ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
        {
            Cache.ApplyChanges(changes, transaction);
        }

        /// <summary>
        /// Removes a transaction from the cache, effectively rolling back any changes associated with the transaction.
        /// </summary>
        /// <param name="transaction">The transaction to remove from the cache.</param>
        public void RemoveTransactionFromCache(Transaction transaction)
        {
            Cache.RemoveTransaction(transaction);
        }

        /// <summary>
        /// Clears all entries from the cache.
        /// </summary>
        public void ClearCache()
        {
            Cache.ClearCache();
        }

        /// <summary>
        /// Releases all resources used by the cache.
        /// </summary>
        public void Dispose()
        {
            Cache.Dispose();
        }
    }
}
