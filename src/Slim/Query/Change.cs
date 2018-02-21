﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Modl.Db.DatabaseProviders;
using Slim.Metadata;

namespace Modl.Db.Query
{
    public abstract class Change : Query<Change>
        //where M : IDbModl, new()
    {
        public Change(DatabaseProvider database, Table table) : base(database, table) { }

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
