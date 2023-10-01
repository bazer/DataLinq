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
                throw new Exception("Call to setter not allowed on an immutable type. Call Mutate() to get mutable object.");
            
            //if (info.Name == "IsNewModel")
            //{
            //    invocation.ReturnValue = false;
            //    return;
            //}

            if (info.CallType == CallType.Method && info.Name == "Mutate")
            {
                invocation.ReturnValue = InstanceFactory.NewMutableRow(this.RowData, Transaction);
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
}