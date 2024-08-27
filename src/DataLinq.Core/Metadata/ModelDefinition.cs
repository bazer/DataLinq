using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;



public class ModelDefinition
{
    //public Type CsType { get; set; }
    //public string CsTypeName { get; set; }
    //public string? CsNamespace { get; set; }
    //public ModelCsType ModelCsType { get; set; }
    public CsTypeDeclaration CsType { get; set; }
    public CsTypeDeclaration ImmutableType { get; set; }
    public CsTypeDeclaration MutableType { get; set; }
    public CsTypeDeclaration[] Interfaces { get; set; }
    public ModelUsing[] Usings { get; set; }
    public DatabaseDefinition Database { get; set; }
    public TableDefinition Table { get; set; }
    public Dictionary<string, RelationProperty> RelationProperties { get; } = new();
    public Dictionary<string, ValueProperty> ValueProperties { get; } = new();
    public Attribute[] Attributes { get; set; }

    public void AddProperty(PropertyDefinition property)
    { 
        if (property is RelationProperty relationProperty)
            RelationProperties.Add(relationProperty.CsName, relationProperty);
        else if (property is ValueProperty valueProperty)
            ValueProperties.Add(valueProperty.CsName, valueProperty);
        else
            throw new NotImplementedException();
    }

    protected bool IsOfType(Type modelType) =>
           modelType == CsType.Type || modelType.BaseType == CsType.Type;

    //public static ModelDefinition? Find(IModel model) =>
    //    DatabaseDefinition
    //    .LoadedDatabases
    //    .Values
    //    .Select(x => x.TableModels.Find(y => y.Model.IsOfType(model.GetType())))
    //    .FirstOrDefault(x => x != null)
    //    ?.Model;

    //public static ModelDefinition? Find<T>() where T : IModel =>
    //    DatabaseDefinition
    //    .LoadedDatabases
    //    .Values
    //    .Select(x => x.TableModels.Find(y => y.Model.IsOfType(typeof(T))))
    //    .FirstOrDefault(x => x != null)
    //    ?.Model;

    public static ModelDefinition? Find(IModel model) =>
        DatabaseDefinition
        .LoadedDatabases
        .Values
        .Select(x => Array.Find(x.TableModels, y => y.Model.IsOfType(model.GetType())))
        .FirstOrDefault(x => x != null)
        ?.Model;

    public static ModelDefinition? Find<T>() where T : IModel =>
        DatabaseDefinition
        .LoadedDatabases
        .Values
        .Select(x => Array.Find(x.TableModels, y => y.Model.IsOfType(typeof(T))))
        .FirstOrDefault(x => x != null)
        ?.Model;



    public override string ToString()
    {
        return $"{CsType.Name} ({Database.DbName}.{Table.DbName})";
    }
}
