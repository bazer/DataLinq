﻿using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataLinq.Metadata
{
    public class Model
    {
        public Type CsType { get; set; }
        //public Type ProxyType { get; set; }
        //public Type MutableProxyType { get; set; }
        public string CsTypeName { get; set; }
        public DatabaseMetadata Database { get; set; }
        public TableMetadata Table { get; set; }
        public List<Property> Properties { get; set; }
        public object[] Attributes { get; set; }

        protected bool IsOfType(Type modelType) =>
               modelType == CsType
            || modelType.BaseType == CsType;
            //|| modelType == MutableProxyType;

        public static Model Find(IModel model) =>
            DatabaseMetadata
            .LoadedDatabases
            .Values
            .Select(x => x.Models.Find(y => y.IsOfType(model.GetType())))
            .FirstOrDefault(x => x != null);
    }
}
