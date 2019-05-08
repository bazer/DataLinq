using Slim.Metadata;
using System.Collections.Generic;

namespace Slim.Query
{
    public abstract class Change : Query<Change>
    {
        protected Change(Transaction transaction, Table table) : base(transaction, table)
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