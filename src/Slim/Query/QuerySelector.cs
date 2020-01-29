using Slim.Interfaces;
using Slim.Metadata;
using Slim.Mutation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Slim.Query
{
    public class QuerySelector
    {
        public Table Table { get; }
        public Transaction Transaction { get; }

        public QuerySelector(Table table, Transaction transaction)
        {
            this.Transaction = transaction;
            this.Table = table;
        }

        public Select Select()
        {
            return new Select(Table, Transaction);
        }

        public Select<T> Select<T>() where T: IModel
        {
            return new Select<T>(Transaction);
        }

        public Update Update()
        {
            return new Update(Table, Transaction);
        }

        public Insert Insert()
        {
            return new Insert(Table, Transaction);
        }

        public Delete Delete()
        {
            return new Delete(Table, Transaction);
        }

    }

    public class QuerySelector<T>
        where T : IModel
    {
        public Table Table { get; }
        public Transaction Transaction { get; }

        public QuerySelector(Transaction transaction)
        {
            this.Transaction = transaction;
            this.Table = transaction.Provider.Metadata.Tables.Single(x => x.Model.CsType == typeof(T));
        }

        public Select<T> Select()
        {
            return new Select<T>(Table, Transaction);
        }

        public Update Update()
        {
            return new Update(Table, Transaction);
        }

        public Insert Insert()
        {
            return new Insert(Table, Transaction);
        }

        public Delete Delete()
        {
            return new Delete(Table, Transaction);
        }

    }
}
