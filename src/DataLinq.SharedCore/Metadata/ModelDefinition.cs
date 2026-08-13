using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public class ModelDefinition(CsTypeDeclaration csType) : IDefinition
{
    private MetadataCollection<CsTypeDeclaration> originalInterfaces = MetadataCollection<CsTypeDeclaration>.Empty;
    private MetadataCollection<ModelUsing> usings = MetadataCollection<ModelUsing>.Empty;
    private MetadataCollection<Attribute> attributes = MetadataCollection<Attribute>.Empty;

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

    public TableModel TableModel { get; private set; } = null!;

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

    internal Delegate? ReadSourceImmutableFactory { get; private set; }

    internal void SetReadSourceImmutableFactoryCore(Delegate readSourceImmutableFactory)
    {
        ThrowIfFrozen();
        ReadSourceImmutableFactory = readSourceImmutableFactory;
    }

    public object? ProviderKeyRowStoreAccessor { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetProviderKeyRowStoreAccessor(object? providerKeyRowStoreAccessor)
    {
        SetProviderKeyRowStoreAccessorCore(providerKeyRowStoreAccessor);
    }

    internal void SetProviderKeyRowStoreAccessorCore(object? providerKeyRowStoreAccessor)
    {
        ThrowIfFrozen();
        ProviderKeyRowStoreAccessor = providerKeyRowStoreAccessor;
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

    public MetadataCollection<CsTypeDeclaration> OriginalInterfaces => originalInterfaces;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetInterfaces(IEnumerable<CsTypeDeclaration> interfaces)
    {
        SetInterfacesCore(interfaces);
    }

    internal void SetInterfacesCore(IEnumerable<CsTypeDeclaration> interfaces)
    {
        ThrowIfFrozen();
        originalInterfaces = new MetadataCollection<CsTypeDeclaration>(interfaces);
    }

    public MetadataCollection<ModelUsing> Usings => usings;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetUsings(IEnumerable<ModelUsing> usings)
    {
        SetUsingsCore(usings);
    }

    internal void SetUsingsCore(IEnumerable<ModelUsing> usings)
    {
        ThrowIfFrozen();
        this.usings = new MetadataCollection<ModelUsing>(usings);
    }

    public MetadataDictionary<string, RelationProperty> RelationProperties { get; } = new();
    public MetadataDictionary<string, ValueProperty> ValueProperties { get; } = new();
    public MetadataCollection<Attribute> Attributes => attributes;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        SetAttributesCore(attributes);
    }

    internal void SetAttributesCore(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        this.attributes = new MetadataCollection<Attribute>(attributes);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddAttribute(Attribute attribute)
    {
        AddAttributeCore(attribute);
    }

    internal void AddAttributeCore(Attribute attribute)
    {
        ThrowIfFrozen();
        attributes = new MetadataCollection<Attribute>(attributes.Append(attribute));
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

    private Dictionary<Attribute, SourceTextSpan>? attributeSourceSpans;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributeSourceSpan(Attribute attribute, SourceTextSpan sourceSpan)
    {
        SetAttributeSourceSpanCore(attribute, sourceSpan);
    }

    internal void SetAttributeSourceSpanCore(Attribute attribute, SourceTextSpan sourceSpan)
    {
        ThrowIfFrozen();
        attributeSourceSpans ??= new Dictionary<Attribute, SourceTextSpan>(AttributeReferenceEqualityComparer.Instance);
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
        if (!CsFile.HasValue ||
            attributeSourceSpans is null ||
            !attributeSourceSpans.TryGetValue(attribute, out var sourceSpan))
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
            RelationProperties.SetCore(relationProperty.PropertyName, relationProperty);
        else if (property is ValueProperty valueProperty)
            // This will add or overwrite the entry for the given property name.
            ValueProperties.SetCore(valueProperty.PropertyName, valueProperty);
        else
            throw new NotImplementedException();
    }

    internal bool IsOfType(Type modelType) =>
           modelType == CsType.Type || modelType.BaseType == CsType.Type;

    public static ModelDefinition? Find(IModel model) => Find(model.GetType());

    public static ModelDefinition? Find<T>() where T : IModel => Find(typeof(T));

    private static ModelDefinition? Find(Type modelType)
    {
        foreach (var database in DatabaseDefinition.LoadedDatabaseValues)
        {
            if (database.TryGetTableModel(modelType, out var tableModel))
                return tableModel.Model;
        }

        return null;
    }

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
