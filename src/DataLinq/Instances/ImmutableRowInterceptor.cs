using Castle.DynamicProxy;
using DataLinq.Metadata;
using DataLinq.Mutation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Instances
{
    internal class ImmutableRowInterceptor : RowInterceptor
    {
        public ImmutableRowInterceptor(RowData rowData) : base(rowData)
        {
        }

        public override void Intercept(IInvocation invocation)
        {
            var info = new InvocationInfo(invocation);

            if (info.CallType == CallType.Set)
                throw new Exception("Call to setter not allowed on an immutable type");

            if (info.MethodType != MethodType.Property)
                throw new NotImplementedException();

            if (info.Name == "IsNew")
            {
                invocation.ReturnValue = false;
                return;
            }

            if (info.Name == "Mutate")
            {
                invocation.ReturnValue = InstanceFactory.NewMutableRow(this.RowData);
                return;
            }

            var name = info.Name;

            //var name = invocation.Method.Name;

            //if (name.StartsWith("set_", StringComparison.Ordinal))
            //    throw new Exception("Call to setter not allowed on an immutable type");

            //if (!name.StartsWith("get_", StringComparison.Ordinal))
            //    throw new NotImplementedException();

            //name = name.Substring(4);



            var property = Properties.Single(x => x.CsName == name);

            if (property.Type == PropertyType.Value)
            {
                invocation.ReturnValue = RowData.GetValue(property.Column.DbName);
            }
            else
            {
                invocation.ReturnValue = GetRelation(info);

                //if (RelationCache == null)
                //    RelationCache = new ConcurrentDictionary<string, object>();

                //if (!RelationCache.TryGetValue(name, out object returnvalue))
                //{
                //    var column = property.Column;
                //    var result = column.Table.Cache.GetRows(new ForeignKey(column, RowData.GetValue(property.RelationPart.Column.DbName)), column.Table.Database.DatabaseProvider.StartTransaction(TransactionType.NoTransaction));

                //    //var column = property.Column;
                //    //var select = new Select(RowData.Table.Database.DatabaseProvider, column.Table)
                //    //    .Where(column.DbName).EqualTo(RowData.Data[property.RelationPart.Column.DbName]);

                //    //var result = select
                //    //    .ReadInstances()
                //    //    .Select(InstanceFactory.NewImmutableRow);

                //    if (property.RelationPart.Type == RelationPartType.ForeignKey)
                //    {
                //        returnvalue = result.SingleOrDefault();

                //        //invocation.ReturnValue = result.SingleOrDefault();
                //    }
                //    else
                //    {
                //        var list = result.ToList();

                //        if (list.Count != 0)
                //        {
                //            returnvalue = typeof(Enumerable)
                //                .GetMethod("Cast")
                //                .MakeGenericMethod(list[0].GetType())
                //                .Invoke(null, new object[] { list });
                //        }
                //    }

                //    RelationCache.TryAdd(name, returnvalue);
                //}

                //invocation.ReturnValue = returnvalue;
            }
        }
        
    }
}