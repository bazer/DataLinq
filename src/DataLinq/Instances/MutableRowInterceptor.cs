using Castle.DynamicProxy;
using DataLinq.Metadata;
using DataLinq.Mutation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Instances
{
    internal class MutableRowInterceptor : RowInterceptor
    {
        MutableRowData MutableRowData { get; }

        public MutableRowInterceptor(RowData rowData, Transaction transaction) : base(rowData, transaction)
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

            if (info.CallType == CallType.Method && info.Name == "GetChanges")
            {
                invocation.ReturnValue = MutableRowData.GetChanges();
                return;
            }

            var property = Properties.Single(x => x.CsName == info.Name);

            if (info.CallType == CallType.Set && info.MethodType == MethodType.Property)
            {
                if (property is ValueProperty valueProperty)
                    MutableRowData.SetValue(valueProperty.Column, info.Value);
                else if (property.Type == PropertyType.Relation)
                    throw new NotImplementedException();
            }
            else if (info.CallType == CallType.Get && info.MethodType == MethodType.Property)
            {
                if (property is ValueProperty valueProperty)
                    invocation.ReturnValue = MutableRowData.GetValue(valueProperty.Column);
                else if (property.Type == PropertyType.Relation)
                    invocation.ReturnValue = GetRelation(info);
                else
                    throw new NotImplementedException();
            }
            else
                throw new NotImplementedException();
        }
    }
}