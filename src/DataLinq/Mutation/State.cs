using DataLinq.Cache;
using DataLinq.Metadata;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.Mutation
{
    public class State: IDisposable 
    {
        //public static ConcurrentDictionary<string, State> ActiveStates { get; } = new ConcurrentDictionary<string, State>();

        public History History { get; set; }
        public DatabaseCache Cache { get; set; }
        public DatabaseProvider Database { get; }

        public State(DatabaseProvider database)
        {
            this.Database = database;
            this.Cache = new DatabaseCache(database);

            //if (!ActiveStates.ContainsKey(database.NameOrAlias) && !ActiveStates.TryAdd(database.NameOrAlias, this))
            //    throw new Exception($"Failed while adding global state for database '{database.NameOrAlias}'.");
        }

        public void ApplyChanges(IEnumerable<StateChange> changes, Transaction transaction = null)
        {
            Cache.ApplyChanges(changes, transaction);
        }

        public void RemoveTransactionFromCache(Transaction transaction)
        {
            Cache.RemoveTransaction(transaction);
        }

        public void ClearCache()
        {
            Cache.ClearCache();
        }

        public void Dispose()
        {
            Cache.Dispose();
        }
    }
}
