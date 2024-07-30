using System;
using System.Collections.Generic;
using System.Linq;
using Castle.DynamicProxy;
using CommunityToolkit.HighPerformance.Buffers;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal class ImmutableRowInterceptor(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess transaction)
    : RowInterceptor(rowData, databaseProvider, transaction)
{
    public override void Intercept(IInvocation invocation)
    {
        var info = new InvocationInfo(invocation);

        if (info.CallType == CallType.Set)
            throw new Exception("Call to setter not allowed on an immutable type. Call Mutate() to get mutable object.");

        if (info.CallType == CallType.Method)
        {
            if (info.Name.Span.SequenceEqual("GetValues".AsSpan()))
            {
                if (info.Arguments?.Length == 1 && info.Arguments[0] is IEnumerable<Column> columns)
                    invocation.ReturnValue = columns.Select(x => new KeyValuePair<Column, object?>(x, RowData.GetValue(x)));
                else
                    invocation.ReturnValue = RowData.GetColumnAndValues();
            }
            else if (info.Name.Span.SequenceEqual("Mutate".AsSpan()))
            {
                invocation.ReturnValue = InstanceFactory.NewMutableRow(this.RowData, databaseProvider, writeTransaction);
            }
            else
            {
                throw new NotImplementedException($"No handler for '{info.Name}' implemented");
            }

            return;
        }

        if (info.MethodType == MethodType.Property)
        {
            if (ValueProperties.TryGetValue(StringPool.Shared.GetOrAdd(info.Name.Span), out var property))
                invocation.ReturnValue = RowData.GetValue(property.Column);
            else
                invocation.ReturnValue = GetRelation(RelationProperties[StringPool.Shared.GetOrAdd(info.Name.Span)]);

            return;
        }

        throw new NotImplementedException($"No handler for '{info.Name}' implemented");
    }
}