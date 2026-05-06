using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class ModelDefinition(CsTypeDeclaration csType) : IDefinition
{
    private CsTypeDeclaration[] originalInterfaces = [];
    private ModelUsing[] usings = [];
    private Attribute[] attributes = [];

    public CsTypeDeclaration CsType { get; private set; } = csType;
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCsType(CsTypeDeclaration csType)
    {
        SetCsTypeCore(csType);
    }

    internal void SetCsTypeCore(CsTypeDeclaration csType)
    {
        ThrowIfFrozen();
        CsType = csType;
    }

    public CsFileDeclaration? CsFile { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCsFile(CsFileDeclaration csFile)
    {
        SetCsFileCore(csFile);
    }

    internal void SetCsFileCore(CsFileDeclaration csFile)
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

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetImmutableType(CsTypeDeclaration immutableType)
    {
        SetImmutableTypeCore(immutableType);
    }

    internal void SetImmutableTypeCore(CsTypeDeclaration immutableType)
    {
        ThrowIfFrozen();
        ImmutableType = immutableType;
    }

    public Delegate? ImmutableFactory { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetImmutableFactory(Delegate immutableFactory)
    {
        SetImmutableFactoryCore(immutableFactory);
    }

    internal void SetImmutableFactoryCore(Delegate immutableFactory)
    {
        ThrowIfFrozen();
        ImmutableFactory = immutableFactory;
    }

    public CsTypeDeclaration? MutableType { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetMutableType(CsTypeDeclaration mutableType)
    {
        SetMutableTypeCore(mutableType);
    }

    internal void SetMutableTypeCore(CsTypeDeclaration mutableType)
    {
        ThrowIfFrozen();
        MutableType = mutableType;
    }

    public CsTypeDeclaration? ModelInstanceInterface { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetModelInstanceInterface(CsTypeDeclaration? interfaceType)
    {
        SetModelInstanceInterfaceCore(interfaceType);
    }

    internal void SetModelInstanceInterfaceCore(CsTypeDeclaration? interfaceType)
    {
        ThrowIfFrozen();
        ModelInstanceInterface = interfaceType;
    }

    public CsTypeDeclaration[] OriginalInterfaces => originalInterfaces.ToArray();

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetInterfaces(IEnumerable<CsTypeDeclaration> interfaces)
    {
        SetInterfacesCore(interfaces);
    }

    internal void SetInterfacesCore(IEnumerable<CsTypeDeclaration> interfaces)
    {
        ThrowIfFrozen();
        originalInterfaces = interfaces.ToArray();
    }

    public ModelUsing[] Usings => usings.ToArray();

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetUsings(IEnumerable<ModelUsing> usings)
    {
        SetUsingsCore(usings);
    }

    internal void SetUsingsCore(IEnumerable<ModelUsing> usings)
    {
        ThrowIfFrozen();
        this.usings = usings.ToArray();
    }

    public MetadataDictionary<string, RelationProperty> RelationProperties { get; } = new();
    public MetadataDictionary<string, ValueProperty> ValueProperties { get; } = new();
    public Attribute[] Attributes => attributes.ToArray();

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        SetAttributesCore(attributes);
    }

    internal void SetAttributesCore(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        this.attributes = attributes.ToArray();
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddAttribute(Attribute attribute)
    {
        AddAttributeCore(attribute);
    }

    internal void AddAttributeCore(Attribute attribute)
    {
        ThrowIfFrozen();
        attributes = [.. attributes, attribute];
    }

    public SourceTextSpan? SourceSpan { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetSourceSpan(SourceTextSpan sourceSpan)
    {
        SetSourceSpanCore(sourceSpan);
    }

    internal void SetSourceSpanCore(SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        SourceSpan = sourceSpan;
    }

    private readonly Dictionary<Attribute, SourceTextSpan> attributeSourceSpans = new(AttributeReferenceEqualityComparer.Instance);

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan)
    {
        SetAttributeSourceSpanCore(attribute, sourceSpan);
    }

    internal void SetAttributeSourceSpanCore(Attribute attribute, SourceTextSpan sourceSpan)
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

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddProperties(IEnumerable<PropertyDefinition> properties)
    {
        AddPropertiesCore(properties);
    }

    internal void AddPropertiesCore(IEnumerable<PropertyDefinition> properties)
    {
        ThrowIfFrozen();

        foreach (var property in properties)
            AddPropertyCore(property);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddProperty(PropertyDefinition property)
    {
        AddPropertyCore(property);
    }

    internal void AddPropertyCore(PropertyDefinition property)
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
        .LoadedDatabaseValues
        .Select(x => Array.Find(x.TableModels, y => y.Model.IsOfType(model.GetType())))
        .FirstOrDefault(x => x != null)
        ?.Model;

    public static ModelDefinition? Find<T>() where T : IModel =>
        DatabaseDefinition
        .LoadedDatabaseValues
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
