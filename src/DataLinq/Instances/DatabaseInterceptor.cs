using System;
using System.Collections.Generic;
using System.Linq;
using Castle.DynamicProxy;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal class DatabaseInterceptor : IInterceptor
{
    public Dictionary<string, object> Data { get; }
    public DataSourceAccess Transaction { get; }

    public DatabaseInterceptor(DataSourceAccess transaction)
    {
        Transaction = transaction;
        Data = ReadDatabase(transaction).ToDictionary(x => x.name, x => x.value);
    }

    private IEnumerable<(string name, object value)> ReadDatabase(DataSourceAccess transaction)
    {
        foreach (var table in transaction.Provider.Metadata.TableModels)
        {
            var dbReadType = typeof(DbRead<>).MakeGenericType(table.Model.CsType);
            var dbRead = Activator.CreateInstance(dbReadType, transaction);

            if (dbRead == null)
                throw new Exception($"Failed to create instance of table model type '{table.Model.CsType}'");

            yield return (table.CsPropertyName, dbRead);
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
