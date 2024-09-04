using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class ModelDefinition(CsTypeDeclaration csType)
{
    public CsTypeDeclaration CsType { get; private set; } = csType;
    public void SetCsType(CsTypeDeclaration csType) => CsType = csType;
    public TableModel TableModel { get; private set; }
    internal void SetTableModel(TableModel tableModel) => TableModel = tableModel;
    public DatabaseDefinition Database => TableModel.Database;
    public TableDefinition Table => TableModel.Table;
    public CsTypeDeclaration? ImmutableType { get; private set; }
    public void SetImmutableType(CsTypeDeclaration immutableType) => ImmutableType = immutableType;
    public CsTypeDeclaration? MutableType { get; private set; }
    public void SetMutableType(CsTypeDeclaration mutableType) => MutableType = mutableType;
    public CsTypeDeclaration[] Interfaces { get; private set; } = [];
    public void SetInterfaces(IEnumerable<CsTypeDeclaration> interfaces) => Interfaces = interfaces.ToArray();
    public ModelUsing[] Usings { get; private set; } = [];
    public void SetUsings(IEnumerable<ModelUsing> usings) => Usings = usings.ToArray();
    public Dictionary<string, RelationProperty> RelationProperties { get; } = new();
    public Dictionary<string, ValueProperty> ValueProperties { get; } = new();
    public Attribute[] Attributes { get; private set; } = [];
    public void SetAttributes(IEnumerable<Attribute> attributes) => Attributes = attributes.ToArray();

    public void AddProperty(PropertyDefinition property)
    { 
        if (property is RelationProperty relationProperty)
            RelationProperties.Add(relationProperty.PropertyName, relationProperty);
        else if (property is ValueProperty valueProperty)
            ValueProperties.Add(valueProperty.PropertyName, valueProperty);
        else
            throw new NotImplementedException();
    }

    protected bool IsOfType(Type modelType) =>
           modelType == CsType.Type || modelType.BaseType == CsType.Type;

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
