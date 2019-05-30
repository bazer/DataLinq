using Slim.Instances;
using Slim.Interfaces;
using Slim.Metadata;
using Slim.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Slim.Mutation
{

    public class StateChange
    {
        public TransactionChangeType Type { get; }
        public IModel Model { get; }
        public Table Table { get; }

        public PrimaryKeys PrimaryKeys { get; }
                    

        public StateChange(IModel model, Table table, TransactionChangeType type)
        {
            Model = model;
            Table = table;
            Type = type;

            PrimaryKeys = new PrimaryKeys(Table.PrimaryKeyColumns.Select(x => x.ValueProperty.GetValue(Model)));
        }
    }
}