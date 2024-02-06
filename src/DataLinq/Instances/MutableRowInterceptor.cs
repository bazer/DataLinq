using System;
using System.Collections.Generic;
using System.Linq;
using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal class MutableRowInterceptor(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction)
    : RowInterceptor(rowData, databaseProvider, transaction)
{
    MutableRowData MutableRowData { get; } = new MutableRowData(rowData);

    public override void Intercept(IInvocation invocation)
    {
        var info = new InvocationInfo(invocation);

        if (info.CallType == CallType.Method)
        {
            if (info.Name.Span.SequenceEqual("GetValues".AsSpan()))
            {
                if (info.Arguments?.Length == 1 && info.Arguments[0] is IEnumerable<Column> columns)
                    invocation.ReturnValue = MutableRowData.GetValues(columns);
                else
                    invocation.ReturnValue = MutableRowData.GetValues();
            }
            else if (info.Name.Span.SequenceEqual("GetChanges".AsSpan()))
            {
                invocation.ReturnValue = MutableRowData.GetChanges();
            }
            else
            {
                throw new NotImplementedException($"No handler for '{info.Name}' implemented");
            }

            return;
        }

        if (info.CallType == CallType.Set && info.MethodType == MethodType.Property)
        {
            if (ValueProperties.TryGetValue(info.Name.ToString(), out var property))
                MutableRowData.SetValue(property.Column, info.Value);
            else
                throw new NotImplementedException();
        }
        else if (info.CallType == CallType.Get && info.MethodType == MethodType.Property)
        {
            if (ValueProperties.TryGetValue(info.Name.ToString(), out var property))
                invocation.ReturnValue = MutableRowData.GetValue(property.Column);
            else
                invocation.ReturnValue = GetRelation(RelationProperties[info.Name.ToString()]);
        }
        else
            throw new NotImplementedException();
    }
}