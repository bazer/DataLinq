using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

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
    public Dictionary<string, RelationProperty> RelationProperties { get; } = new();
    public Dictionary<string, ValueProperty> ValueProperties { get; } = new();
    public Attribute[] Attributes { get; set; }

    public void AddProperty(Property property)
    { 
        if (property is RelationProperty relationProperty)
            RelationProperties.Add(relationProperty.CsName, relationProperty);
        else if (property is ValueProperty valueProperty)
            ValueProperties.Add(valueProperty.CsName, valueProperty);
        else
            throw new NotImplementedException();
    }

    protected bool IsOfType(Type modelType) =>
           modelType == CsType || modelType.BaseType == CsType;

    public static ModelMetadata? Find(IModel model) =>
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
