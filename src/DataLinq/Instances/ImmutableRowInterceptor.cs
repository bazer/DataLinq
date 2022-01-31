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
        public ImmutableRowInterceptor(RowData rowData, Transaction transaction) : base(rowData, transaction)
        {
        }

        public override void Intercept(IInvocation invocation)
        {
            var info = new InvocationInfo(invocation);

            if (info.CallType == CallType.Set)
                throw new Exception("Call to setter not allowed on an immutable type");

            if (info.MethodType != MethodType.Property)
                throw new NotImplementedException();

            //if (info.Name == "IsNew")
            //{
            //    invocation.ReturnValue = false;
            //    return;
            //}

            if (info.Name == "Mutate")
            {
                invocation.ReturnValue = InstanceFactory.NewMutableRow(this.RowData, Transaction);
                return;
            }

            var name = info.Name;
            var property = Properties.Single(x => x.CsName == name);

            if (property.Type == PropertyType.Value)
            {
                invocation.ReturnValue = RowData.GetValue(property.Column.DbName);
            }
            else
            {
                invocation.ReturnValue = GetRelation(info);
            }
        }
        
    }
}