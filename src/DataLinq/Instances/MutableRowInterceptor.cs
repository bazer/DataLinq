using System;
using System.Collections.Generic;
using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal class MutableRowInterceptor : RowInterceptor
{
    MutableRowData MutableRowData { get; }

    public MutableRowInterceptor(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction) : base(rowData, databaseProvider, transaction)
    {
        this.MutableRowData = new MutableRowData(rowData);
    }

    public override void Intercept(IInvocation invocation)
    {
        var info = new InvocationInfo(invocation);

        //if (info.Name == "IsNewModel")
        //{
        //    invocation.ReturnValue = false;
        //    return;
        //}

        if (info.CallType == CallType.Method && info.Name == "GetValues")
        {
            if (info.Arguments?.Length == 1 && info.Arguments[0] is IEnumerable<Column> columns)
                invocation.ReturnValue = MutableRowData.GetValues(columns);
            else
                invocation.ReturnValue = MutableRowData.GetValues();

            return;
        }

        if (info.CallType == CallType.Method && info.Name == "GetChanges")
        {
            invocation.ReturnValue = MutableRowData.GetChanges();
            return;
        }

        //var property = ValueProperties[info.Name];

        if (info.CallType == CallType.Set && info.MethodType == MethodType.Property)
        {
            if (ValueProperties.TryGetValue(info.Name, out var property))
                MutableRowData.SetValue(property.Column, info.Value);
            else
                throw new NotImplementedException();
        }
        else if (info.CallType == CallType.Get && info.MethodType == MethodType.Property)
        {
            if (ValueProperties.TryGetValue(info.Name, out var property))
                invocation.ReturnValue = MutableRowData.GetValue(property.Column);
            else //if (property.Type == PropertyType.Relation)
                invocation.ReturnValue = GetRelation(info);
            //else
            //    throw new NotImplementedException();
        }
        else
            throw new NotImplementedException();
    }
}