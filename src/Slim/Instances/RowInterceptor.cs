using Castle.DynamicProxy;
using Slim.Metadata;
using Slim.Mutation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Slim.Instances
{
    internal abstract class RowInterceptor : IInterceptor
    {
        protected RowInterceptor(RowData rowData)
        {
            RowData = rowData;
        }

        protected List<Property> Properties =>
            RowData.Table.Model.Properties;

        protected RowData RowData { get; }
        protected ConcurrentDictionary<string, object> RelationCache;

        public abstract void Intercept(IInvocation invocation);

        protected object GetRelation(InvocationInfo info)
        {
            var property = Properties.Single(x => x.CsName == info.Name);

            if (RelationCache == null)
                RelationCache = new ConcurrentDictionary<string, object>();

            if (!RelationCache.TryGetValue(info.Name, out object returnvalue))
            {
                var column = property.Column;
                var result = column.Table.Cache.GetRows(new ForeignKey(column, RowData.GetValue(property.RelationPart.Column.DbName)), column.Table.Database.DatabaseProvider.StartTransaction(TransactionType.NoTransaction));

                //var column = property.Column;
                //var select = new Select(RowData.Table.Database.DatabaseProvider, column.Table)
                //    .Where(column.DbName).EqualTo(RowData.Data[property.RelationPart.Column.DbName]);

                //var result = select
                //    .ReadInstances()
                //    .Select(InstanceFactory.NewImmutableRow);

                if (property.RelationPart.Type == RelationPartType.ForeignKey)
                {
                    returnvalue = result.SingleOrDefault();

                    //invocation.ReturnValue = result.SingleOrDefault();
                }
                else
                {
                    var list = result.ToList();

                    if (list.Count != 0)
                    {
                        returnvalue = typeof(Enumerable)
                            .GetMethod("Cast")
                            .MakeGenericMethod(list[0].GetType())
                            .Invoke(null, new object[] { list });
                    }
                }

                RelationCache.TryAdd(info.Name, returnvalue);
            }

            return returnvalue;

        }
    }

    internal struct InvocationInfo
    {
        internal CallType CallType { get; }
        internal MethodType MethodType { get; }
        internal string Name { get; }
        internal object Value { get; }

        internal InvocationInfo(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                this.CallType = CallType.Set;
            else if (name.StartsWith("get_", StringComparison.Ordinal))
                this.CallType = CallType.Get;
            //else if (name == "GetChanges")
            //    this.CallType = CallType.Get;
            else
                throw new NotImplementedException();

            //if (this.CallType == CallType.Get && name == "GetChanges")
            //{
            //    this.MethodType = MethodType.Property;
            //    this.Property = name;
            //}
            if ((this.CallType == CallType.Get && invocation.Arguments.Length == 1) || (this.CallType == CallType.Set && invocation.Arguments.Length == 2))
            {
                this.MethodType = MethodType.Indexer;
                this.Name = invocation.Arguments[0] as string;
            }
            else
            {
                this.MethodType = MethodType.Property;
                this.Name = name.Substring(4);
            }

            if (this.CallType == CallType.Set && this.MethodType == MethodType.Property)
                this.Value = invocation.Arguments[0];
            else if (this.CallType == CallType.Set && this.MethodType == MethodType.Indexer)
                this.Value = invocation.Arguments[1];
            else
                this.Value = null;
        }
    }

    internal enum CallType
    {
        Get,
        Set
    }

    internal enum MethodType
    {
        Property,
        Indexer,
        Changes
    }
}