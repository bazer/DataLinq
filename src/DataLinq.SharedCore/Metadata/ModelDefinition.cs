using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class ModelDefinition(CsTypeDeclaration csType) : IDefinition
{
    public CsTypeDeclaration CsType { get; private set; } = csType;
    public bool IsFrozen { get; private set; }

    public void SetCsType(CsTypeDeclaration csType)
    {
        ThrowIfFrozen();
        CsType = csType;
    }

    public CsFileDeclaration? CsFile { get; private set; }

    public void SetCsFile(CsFileDeclaration csFile)
    {
        ThrowIfFrozen();
        CsFile = csFile;
    }

    public TableModel TableModel { get; private set; }

    internal void SetTableModel(TableModel tableModel)
    {
        ThrowIfFrozen();
        TableModel = tableModel;
    }

    public DatabaseDefinition Database => TableModel.Database;
    public TableDefinition Table => TableModel.Table;
    public CsTypeDeclaration? ImmutableType { get; private set; }

    public void SetImmutableType(CsTypeDeclaration immutableType)
    {
        ThrowIfFrozen();
        ImmutableType = immutableType;
    }

    public Delegate? ImmutableFactory { get; private set; }

    public void SetImmutableFactory(Delegate immutableFactory)
    {
        ThrowIfFrozen();
        ImmutableFactory = immutableFactory;
    }

    public CsTypeDeclaration? MutableType { get; private set; }

    public void SetMutableType(CsTypeDeclaration mutableType)
    {
        ThrowIfFrozen();
        MutableType = mutableType;
    }

    public CsTypeDeclaration? ModelInstanceInterface { get; private set; }

    public void SetModelInstanceInterface(CsTypeDeclaration? interfaceType)
    {
        ThrowIfFrozen();
        ModelInstanceInterface = interfaceType;
    }

    public CsTypeDeclaration[] OriginalInterfaces { get; private set; } = [];

    public void SetInterfaces(IEnumerable<CsTypeDeclaration> interfaces)
    {
        ThrowIfFrozen();
        OriginalInterfaces = interfaces.ToArray();
    }

    public ModelUsing[] Usings { get; private set; } = [];

    public void SetUsings(IEnumerable<ModelUsing> usings)
    {
        ThrowIfFrozen();
        Usings = usings.ToArray();
    }

    public MetadataDictionary<string, RelationProperty> RelationProperties { get; } = new();
    public MetadataDictionary<string, ValueProperty> ValueProperties { get; } = new();
    public Attribute[] Attributes { get; private set; } = [];

    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        Attributes = attributes.ToArray();
    }

    public void AddAttribute(Attribute attribute)
    {
        ThrowIfFrozen();
        Attributes = [.. Attributes, attribute];
    }

    public SourceTextSpan? SourceSpan { get; private set; }

    public void SetSourceSpan(SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        SourceSpan = sourceSpan;
    }

    private readonly Dictionary<Attribute, SourceTextSpan> attributeSourceSpans = new(AttributeReferenceEqualityComparer.Instance);

    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        attributeSourceSpans[attribute] = sourceSpan;
    }

    public SourceLocation? GetSourceLocation()
    {
        if (!CsFile.HasValue)
            return null;

        return new SourceLocation(CsFile.Value, SourceSpan);
    }

    public SourceLocation? GetAttributeSourceLocation(Attribute attribute)
    {
        if (!CsFile.HasValue || !attributeSourceSpans.TryGetValue(attribute, out var sourceSpan))
            return null;

        return new SourceLocation(CsFile.Value, sourceSpan);
    }

    public void AddProperties(IEnumerable<PropertyDefinition> properties)
    {
        ThrowIfFrozen();

        foreach (var property in properties)
            AddProperty(property);
    }

    public void AddProperty(PropertyDefinition property)
    {
        ThrowIfFrozen();

        if (property is RelationProperty relationProperty)
            // This will add or overwrite the entry for the given property name.
            RelationProperties[relationProperty.PropertyName] = relationProperty;
        else if (property is ValueProperty valueProperty)
            // This will add or overwrite the entry for the given property name.
            ValueProperties[valueProperty.PropertyName] = valueProperty;
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

    public CsTypeDeclaration CsTypeOrInterface => ModelInstanceInterface ?? CsType;

    public override string ToString()
    {
        if (TableModel == null)
            return $"{CsType.Name}";
        else
            return $"{CsType.Name} ({Database?.DbName}.{Table?.DbName})";
    }

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;

        foreach (var property in ValueProperties.Values)
            property.Freeze();

        foreach (var property in RelationProperties.Values)
            property.Freeze();

        ValueProperties.Freeze();
        RelationProperties.Freeze();
    }

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}
