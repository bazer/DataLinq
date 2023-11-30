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
        this.databaseProvider = databaseProvider;
        this.writeTransaction = transaction == null || transaction.Type == TransactionType.ReadOnly ? null : transaction;
    }

    protected List<Property> Properties =>
        RowData.Table.Model.Properties;

    protected RowData RowData { get; }
    //protected ConcurrentDictionary<string, object> RelationCache;
    protected Transaction? writeTransaction;
    protected IDatabaseProvider databaseProvider;

    public abstract void Intercept(IInvocation invocation);

    protected Transaction GetTransaction()
    {
        if (writeTransaction != null && (writeTransaction.Status == DatabaseTransactionStatus.Committed || writeTransaction.Status == DatabaseTransactionStatus.RolledBack))
            writeTransaction = null;

        return writeTransaction ?? databaseProvider.StartTransaction(TransactionType.ReadOnly);
    }

    protected object GetRelation(InvocationInfo info)
    {
        var property = Properties
            .OfType<RelationProperty>()
            .Single(x => x.CsName == info.Name);

        //if (RelationCache == null)
        //    RelationCache = new ConcurrentDictionary<string, object>();

        //if (!RelationCache.TryGetValue(info.Name, out object returnvalue))
        //{

        //if (writeTransaction != null && (writeTransaction.Status == DatabaseTransactionStatus.Committed || writeTransaction.Status == DatabaseTransactionStatus.RolledBack))
        //    writeTransaction = null;

        //var transaction = writeTransaction ?? databaseProvider.StartTransaction(TransactionType.ReadOnly);

        //if (writeTransaction == null || (writeTransaction.Status == DatabaseTransactionStatus.Committed || writeTransaction.Status == DatabaseTransactionStatus.RolledBack))
        //    writeTransaction = databaseProvider.StartTransaction(TransactionType.ReadOnly);

        var transaction = GetTransaction();

        var otherSide = property.RelationPart.GetOtherSide();
        var result = databaseProvider
            .GetTableCache(otherSide.Column.Table)
            .GetRows(new ForeignKey(otherSide.Column, RowData.GetValue(property.RelationPart.Column.DbName)), transaction);

        object returnvalue;
        if (property.RelationPart.Type == RelationPartType.ForeignKey)
        {
            returnvalue = result.SingleOrDefault();
        }
        else
        {
            var listType = property.CsType.GetTypeInfo().GenericTypeArguments[0];

            returnvalue = typeof(Enumerable)
                .GetMethod("Cast")
                .MakeGenericMethod(listType)
                .Invoke(null, new object[] { result });
        }

        //    RelationCache.TryAdd(info.Name, returnvalue);
        //}

        return returnvalue;
    }
}