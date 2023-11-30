using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata
{
    public enum ModelCsType
    {
        Class,
        Record,
        Interface
    }

    public class ModelNamespace
    {
        public string FullNamespaceName { get; set; }
    }

    public class ModelInterface
    {
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
    }

    public class ModelMetadata
    {
        public Type CsType { get; set; }
        public string CsTypeName { get; set; }
        public ModelCsType ModelCsType { get; set; }
        public ModelInterface[] Interfaces { get; set; }
        public ModelNamespace[] Namespaces { get; set; }
        public DatabaseMetadata Database { get; set; }
        public TableMetadata Table { get; set; }
        public List<Property> Properties { get; set; } = new List<Property>();
        public IEnumerable<RelationProperty> RelationProperties => Properties
            .OfType<RelationProperty>();
        public IEnumerable<ValueProperty> ValueProperties => Properties
            .OfType<ValueProperty>();
        public Attribute[] Attributes { get; set; }

        protected bool IsOfType(Type modelType) =>
               modelType == CsType || modelType.BaseType == CsType;

        public static ModelMetadata Find(IModel model) =>
            DatabaseMetadata
            .LoadedDatabases
            .Values
            .Select(x => x.TableModels.Find(y => y.Model.IsOfType(model.GetType())))
            .FirstOrDefault(x => x != null)
            ?.Model;

        public override string ToString()
        {
            return $"{CsTypeName} ({Database.DbName}.{Table.DbName})";
        }
    }
}
