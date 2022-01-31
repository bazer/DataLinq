﻿using Castle.DynamicProxy;
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
            var property = Properties.Single(x => x.CsName == info.Name);

            if (info.CallType == CallType.Set && info.MethodType == MethodType.Property)
            {
                MutableRowData.SetValue(property.Column.DbName, info.Value);
            }
            else if (info.CallType == CallType.Get && info.MethodType == MethodType.Property)
            {
                if (property.Type == PropertyType.Value)
                    invocation.ReturnValue = MutableRowData.GetValue(property.Column.DbName);
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