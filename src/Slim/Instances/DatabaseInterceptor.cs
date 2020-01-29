using Castle.DynamicProxy;
using Slim.Interfaces;
using Slim.Metadata;
using Slim.Mutation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Slim.Instances
{
    internal class DatabaseInterceptor : IInterceptor
    {
        public Dictionary<string, object> Data { get; }
        public Transaction Transaction { get; }

        public DatabaseInterceptor(Transaction transaction)
        {
            Transaction = transaction;
            Data = ReadDatabase(transaction).ToDictionary(x => x.name, x => x.value);
        }

        private IEnumerable<(string name, object value)> ReadDatabase(Transaction transaction)
        {
            foreach (var table in transaction.Provider.Metadata.Tables)
            {
                var dbReadType = typeof(DbRead<>).MakeGenericType(table.Model.CsType);
                var dbRead = Activator.CreateInstance(dbReadType, transaction);

                yield return (table.Model.CsTypeName, dbRead);
            }
        }

        public void Intercept(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                throw new Exception("Call to setter not allowed on an immutable type");

            if (!name.StartsWith("get_", StringComparison.Ordinal))
                throw new NotImplementedException();

            name = name.Substring(4);

            invocation.ReturnValue = Data[name];
        }
    }
}
