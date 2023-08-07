using Castle.DynamicProxy;
using DataLinq.Metadata;
using DataLinq.Mutation;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DataLinq.Instances
{
    internal abstract class RowInterceptor : IInterceptor
    {
        protected RowInterceptor(RowData rowData, Transaction transaction)
        {
            RowData = rowData;
            Transaction = transaction;
        }

        protected List<Property> Properties =>
            RowData.Table.Model.Properties;

        protected RowData RowData { get; }
        //protected ConcurrentDictionary<string, object> RelationCache;
        protected Transaction Transaction;

        public abstract void Intercept(IInvocation invocation);

        protected object GetRelation(InvocationInfo info)
        {
            var property = Properties
                .OfType<RelationProperty>()
                .Single(x => x.CsName == info.Name);

            //if (RelationCache == null)
            //    RelationCache = new ConcurrentDictionary<string, object>();

            //if (!RelationCache.TryGetValue(info.Name, out object returnvalue))
            //{

            if (Transaction.Type != TransactionType.ReadOnly && (Transaction.Status == DatabaseTransactionStatus.Committed || Transaction.Status == DatabaseTransactionStatus.RolledBack))
                Transaction = Transaction.Provider.StartTransaction(TransactionType.ReadOnly);

            var otherSide = property.RelationPart.GetOtherSide();
            var result = Transaction.Provider
                .GetTableCache(otherSide.Column.Table)
                .GetRows(new ForeignKey(otherSide.Column, RowData.GetValue(property.RelationPart.Column.DbName)), Transaction);

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
}