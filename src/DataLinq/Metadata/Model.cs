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
        public Type ProxyType { get; set; }
        public Type MutableProxyType { get; set; }
        public string CsTypeName { get; set; }
        public DatabaseMetadata Database { get; set; }
        public Table Table { get; set; }
        public List<Property> Properties { get; set; }
        public object[] Attributes { get; set; }

        public bool IsOfType(Type modelType) =>
               modelType == CsType
            || modelType == ProxyType
            || modelType == MutableProxyType;

        public static Model Find(Type type) =>
            State
            .ActiveStates
            .Values
            .Select(x => x.Database.Models.Find(y => y.IsOfType(type)))
            .FirstOrDefault(x => x != null);

        public static Model Find(IModel model) =>
            Find(model.GetType());
    }
}
