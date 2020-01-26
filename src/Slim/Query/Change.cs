using Slim.Metadata;
using Slim.Mutation;
using System.Collections.Generic;

namespace Slim.Query
{
    public abstract class Change : Query<Change>
    {
        protected Change(Table table, Transaction transaction) : base(table, transaction)
        {
        }

        protected Dictionary<string, object> withList = new Dictionary<string, object>();

        public Change With(string key, string value)
        {
            return With<string>(key, value);
        }

        public Change With<V>(string key, V value)
        {
            withList.Add(key, value);
            return this;
        }
    }
}