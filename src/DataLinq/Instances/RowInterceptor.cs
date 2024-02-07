using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal abstract class RowInterceptor : IInterceptor
{
    protected RowInterceptor(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction)
    {
        RowData = rowData;
        //RelationProperties = RowData.Table.Model.Properties.DistinctBy(x => x.CsName).OfType<RelationProperty>().ToDictionary(x => x.CsName);
        //ValueProperties = RowData.Table.Model.Properties.DistinctBy(x => x.CsName).OfType<ValueProperty>().ToDictionary(x => x.CsName);
        this.databaseProvider = databaseProvider;
        this.writeTransaction = transaction == null || transaction.Type == TransactionType.ReadOnly ? null : transaction;
    }

    protected Dictionary<string, RelationProperty> RelationProperties => RowData.Table.Model.RelationProperties;
    protected Dictionary<string, ValueProperty> ValueProperties => RowData.Table.Model.ValueProperties;
    protected RowData RowData { get; }
    protected Transaction? writeTransaction;
    protected IDatabaseProvider databaseProvider;

    private static readonly ConcurrentDictionary<Type, MethodInfo> castMethodCache = new();


    public abstract void Intercept(IInvocation invocation);

    protected Transaction? GetTransaction()
    {
        if (writeTransaction != null && (writeTransaction.Status == DatabaseTransactionStatus.Committed || writeTransaction.Status == DatabaseTransactionStatus.RolledBack))
            writeTransaction = null;

        return writeTransaction; //databaseProvider.StartTransaction(TransactionType.ReadOnly);
    }

    protected object? GetRelation(RelationProperty property)
    {
        var otherSide = property.RelationPart.GetOtherSide();
        var result = databaseProvider
            .GetTableCache(otherSide.ColumnIndex.Table)
            .GetRows(new ForeignKey(otherSide.ColumnIndex, RowData.GetValues(property.RelationPart.ColumnIndex.Columns).ToArray()), property, GetTransaction());

        object? returnvalue;
        if (property.RelationPart.Type == RelationPartType.ForeignKey)
        {
            returnvalue = result.SingleOrDefault();
        }
        else
        {
            var listType = property.CsType.GetTypeInfo().GenericTypeArguments[0];

            // Use the cache
            var castMethod = castMethodCache.GetOrAdd(listType, (Type lt) =>
            {
                return typeof(Enumerable)
                    .GetMethod("Cast", BindingFlags.Static | BindingFlags.Public)
                    .MakeGenericMethod(lt);
            });

            returnvalue = castMethod.Invoke(null, new object[] { result });

            //var listType = property.CsType.GetTypeInfo().GenericTypeArguments[0];

            //returnvalue = typeof(Enumerable)
            //    .GetMethod("Cast")
            //    .MakeGenericMethod(listType)
            //    .Invoke(null, new object[] { result });
        }

        return returnvalue;
    }
}