using System;
using System.Collections.Generic;
using System.Linq;
using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal class ImmutableRowInterceptor : RowInterceptor
{
    public ImmutableRowInterceptor(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction) : base(rowData, databaseProvider, transaction)
    {
    }

    public override void Intercept(IInvocation invocation)
    {
        var info = new InvocationInfo(invocation);

        if (info.CallType == CallType.Set)
            throw new Exception("Call to setter not allowed on an immutable type. Call Mutate() to get mutable object.");

        //if (info.Name == "IsNewModel")
        //{
        //    invocation.ReturnValue = false;
        //    return;
        //}

        if (info.CallType == CallType.Method && info.Name == "GetValues")
        {
            if (info.Arguments?.Length == 1 && info.Arguments[0] is IEnumerable<Column> columns)
                invocation.ReturnValue = columns.Select(x => new KeyValuePair<Column, object>(x, RowData.GetValue(x)));
            else
                invocation.ReturnValue = RowData.Columns.Select(x => new KeyValuePair<Column, object>(x, RowData.GetValue(x)));

            return;
        }

        if (info.CallType == CallType.Method && info.Name == "GetValues")
        {
            invocation.ReturnValue = RowData.Columns.Select(x => new KeyValuePair<Column, object>(x, RowData.GetValue(x)));
            return;
        }

        if (info.CallType == CallType.Method && info.Name == "Mutate")
        {
            invocation.ReturnValue = InstanceFactory.NewMutableRow(this.RowData, databaseProvider, writeTransaction);
            return;
        }

        if (info.MethodType == MethodType.Property)
        {
            var name = info.Name;
            var property = Properties.Single(x => x.CsName == name);

            if (property is ValueProperty valueProperty)
            {
                invocation.ReturnValue = RowData.GetValue(valueProperty.Column.DbName);
            }
            else
            {
                invocation.ReturnValue = GetRelation(info);
            }

            return;
        }

        throw new NotImplementedException($"No handler for '{info.Name}' implemented");
    }
}