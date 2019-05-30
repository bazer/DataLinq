using Slim.Cache;
using Slim.Metadata;
using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Mutation
{
    public class State
    {
        public History History { get; set; }
        public DatabaseCache Cache { get; set; }
        public Database Database { get; }

        public State(Database database)
        {
            this.Database = database;
            this.Cache = new DatabaseCache(database);
        }

        public void ApplyChanges(params StateChange[] changes)
        {
            Cache.Apply(changes);
        }
    }
}
