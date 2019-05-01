using Castle.DynamicProxy;
using Slim.Interfaces;
using Slim.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Slim.Instances
{
    internal class DatabaseInterceptor : IInterceptor
    {
        public Dictionary<string, object> Data { get; }
        public Transaction DatabaseProvider { get; }

        public DatabaseInterceptor(Transaction databaseProvider)
        {
            DatabaseProvider = databaseProvider;
            Data = ReadDatabase(databaseProvider).ToDictionary(x => x.name, x => x.value);
        }

        private IEnumerable<(string name, object value)> ReadDatabase(Transaction databaseProvider)
        {
            foreach (var table in databaseProvider.DatabaseProvider.Database.Tables)
            {
                var dbReadType = typeof(DbRead<>).MakeGenericType(table.Model.CsType);
                var dbRead = Activator.CreateInstance(dbReadType, databaseProvider);

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

            //if (name == "Modl")
            //    invocation.ReturnValue = ModlData;
            //else if (name == "IsMutable")
            //    invocation.ReturnValue = false;
            //else if (name == "IsNew")
            //    invocation.ReturnValue = ModlData.Backer.IsNew;
            //else
            //{
            //    var value = ModlData.Backer.SimpleValueBacker.GetValue(name).Get();
            //    invocation.ReturnValue = value;
            //}
        }
    }
}
