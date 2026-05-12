using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Metadata;

public enum PropertyType
{
    Value,
    Relation
}

public abstract class PropertyDefinition(string propertyName, CsTypeDeclaration csType, ModelDefinition model, IEnumerable<Attribute> attributes) : IDefinition
{
    private MetadataCollection<Attribute> attributeArray = new(attributes);

    public MetadataCollection<Attribute> Attributes => attributeArray;
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAttributes(IEnumerable<Attribute> attributes)
    {
        SetAttributesCore(attributes);
    }

    internal void SetAttributesCore(IEnumerable<Attribute> attributes)
    {
        ThrowIfFrozen();
        attributeArray = new MetadataCollection<Attribute>(attributes);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddAttribute(Attribute attribute)
    {
        AddAttributeCore(attribute);
    }

    internal void AddAttributeCore(Attribute attribute)
    {
        ThrowIfFrozen();
        attributeArray = new MetadataCollection<Attribute>(attributeArray.Append(attribute));
    }

    public string PropertyName { get; private set; } = propertyName;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetPropertyName(string propertyName)
    {
        SetPropertyNameCore(propertyName);
    }

    internal void SetPropertyNameCore(string propertyName)
    {
        ThrowIfFrozen();
        PropertyName = propertyName;
    }

    public CsTypeDeclaration CsType { get; private set; } = csType;

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

    public bool CsNullable { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCsNullable(bool csNullable = true)
    {
        SetCsNullableCore(csNullable);
    }

    internal void SetCsNullableCore(bool csNullable = true)
    {
        ThrowIfFrozen();
        CsNullable = csNullable;
    }

    public ModelDefinition Model { get; private set; } = model;
    public PropertyType Type { get; protected private set; }
    public PropertySourceInfo? SourceInfo { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetSourceInfo(PropertySourceInfo sourceInfo)
    {
        SetSourceInfoCore(sourceInfo);
    }

    internal void SetSourceInfoCore(PropertySourceInfo sourceInfo)
    {
        ThrowIfFrozen();
        SourceInfo = sourceInfo;
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

    public SourceLocation? GetAttributeSourceLocation(Attribute attribute)
    {
        if (!CsFile.HasValue ||
            attributeSourceSpans is null ||
            !attributeSourceSpans.TryGetValue(attribute, out var sourceSpan))
            return null;

        return new SourceLocation(CsFile.Value, sourceSpan);
    }

    public CsFileDeclaration? CsFile => Model?.CsFile;

    public override string ToString() => $"Property: {CsType.Name} {PropertyName}";

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;

        foreach (var defaultAttribute in attributeArray.OfType<DefaultAttribute>())
            defaultAttribute.Freeze();
    }

    protected void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);

}

public class ValueProperty : PropertyDefinition
{
    public ColumnDefinition Column { get; private set; } = null!;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetColumn(ColumnDefinition column)
    {
        SetColumnCore(column);
    }

    internal void SetColumnCore(ColumnDefinition column)
    {
        ThrowIfFrozen();
        Column = column;
    }

    public int? CsSize { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetCsSize(int? csSize)
    {
        SetCsSizeCore(csSize);
    }

    internal void SetCsSizeCore(int? csSize)
    {
        ThrowIfFrozen();
        CsSize = csSize;
    }

    public EnumProperty? EnumProperty { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetEnumProperty(EnumProperty enumProperty)
    {
        SetEnumPropertyCore(enumProperty);
    }

    internal void SetEnumPropertyCore(EnumProperty enumProperty)
    {
        ThrowIfFrozen();
        EnumProperty = enumProperty;
    }

    public ValueProperty(string propertyName, CsTypeDeclaration csType, ModelDefinition model, IEnumerable<Attribute> attributes) : base(propertyName, csType, model, attributes)
    {
        Type = PropertyType.Value;
    }

    public bool HasDefaultValue() => Attributes.Any(x => x is DefaultAttribute);

    public DefaultAttribute? GetDefaultAttribute() => Attributes
            .Where(x => x is DefaultAttribute)
            .Select(x => x as DefaultAttribute)
            .FirstOrDefault();

    public string? GetDefaultValue()
    {
        var defaultAttr = GetDefaultAttribute();

        if (defaultAttr is DefaultCurrentTimestampAttribute)
        {
            return CsType.Name switch
            {
                "DateOnly" => "DateOnly.FromDateTime(DateTime.Now)",
                "TimeOnly" => "TimeOnly.FromDateTime(DateTime.Now)",
                "DateTime" => "DateTime.Now",
                _ => "DateTime.Now"
            };
        }
        else if (defaultAttr is DefaultNewUUIDAttribute defaultNewUUID)
        {
            if (CsType.Name != "Guid" && CsType.Name != "System.Guid")
                throw new InvalidOperationException($"DefaultNewUUIDAttribute can only be used with Guid type, but found {CsType.Name}.");

            return defaultNewUUID.Version switch
            {
                UUIDVersion.Version4 => "Guid.NewGuid()",
                UUIDVersion.Version7 => "Guid.CreateVersion7()",
                _ => throw new NotImplementedException(),
            };
        }
        else if (defaultAttr is DefaultSqlAttribute)
        {
            return null;
        }

        return defaultAttr?.Value.ToString();
    }

    public string? GetDefaultValueCode()
    {
        var defaultAttr = GetDefaultAttribute();

        if (defaultAttr is DefaultCurrentTimestampAttribute || defaultAttr is DefaultNewUUIDAttribute)
            return GetDefaultValue();

        if (defaultAttr is DefaultSqlAttribute)
            return null;

        if (defaultAttr == null)
            return null;

        if (!string.IsNullOrWhiteSpace(defaultAttr.CodeExpression))
            return defaultAttr.CodeExpression;

        if (EnumProperty != null)
            return FormatEnumDefaultValue(defaultAttr.Value);

        return FormatDefaultValueForPropertyType(defaultAttr.Value);
    }

    private string FormatEnumDefaultValue(object value)
    {
        var enumProperty = EnumProperty!.Value;
        var enumTypeName = $"{(enumProperty.DeclaredInClass ? $"{Model.CsType.Name}." : "")}{CsType.Name}";
        var numericValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        var enumMember = enumProperty.CsValuesOrDbValues.FirstOrDefault(x => x.value == numericValue);

        if (enumMember.name != null)
            return $"{enumTypeName}.{enumMember.name}";

        return $"({enumTypeName}){numericValue.ToString(CultureInfo.InvariantCulture)}";
    }

    private string FormatDefaultValueForPropertyType(object value)
    {
        return CsType.Name switch
        {
            "string" => CSharpLiteralFormatter.FormatString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            "char" => CSharpLiteralFormatter.FormatChar(Convert.ToChar(value, CultureInfo.InvariantCulture)),
            "bool" => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false",
            "sbyte" => $"(sbyte){Convert.ToSByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            "byte" => $"(byte){Convert.ToByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            "short" => $"(short){Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            "ushort" => $"(ushort){Convert.ToUInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            "int" => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            "uint" => $"{Convert.ToUInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}U",
            "long" => $"{Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}L",
            "ulong" => $"{Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}UL",
            "float" => $"{Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)}F",
            "double" => $"{Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)}D",
            "decimal" => $"{Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}M",
            "DateTime" => $"DateTime.Parse({CSharpLiteralFormatter.FormatString(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))})",
            "DateTimeOffset" => $"DateTimeOffset.Parse({CSharpLiteralFormatter.FormatString(((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))})",
            "TimeSpan" => $"TimeSpan.Parse({CSharpLiteralFormatter.FormatString(((TimeSpan)value).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture))})",
            "DateOnly" => $"DateOnly.Parse({CSharpLiteralFormatter.FormatString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)})",
            "TimeOnly" => $"TimeOnly.Parse({CSharpLiteralFormatter.FormatString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)})",
            "Guid" or "System.Guid" => $"Guid.Parse({CSharpLiteralFormatter.FormatString(((Guid)value).ToString())})",
            _ => value.ToString() ?? string.Empty,
        };
    }
}

public record struct EnumProperty
{
    private readonly MetadataCollection<(string name, int value)>? dbEnumValues;
    private readonly MetadataCollection<(string name, int value)>? csEnumValues;

    public EnumProperty(IEnumerable<(string name, int value)>? enumValues = null, IEnumerable<(string name, int value)>? csEnumValues = null, bool declaredInClass = true)
    {
        dbEnumValues = new MetadataCollection<(string name, int value)>(enumValues ?? []);
        this.csEnumValues = new MetadataCollection<(string name, int value)>(csEnumValues ?? []);
        DeclaredInClass = declaredInClass;
    }

    public MetadataCollection<(string name, int value)> DbEnumValues =>
        dbEnumValues ?? MetadataCollection<(string name, int value)>.Empty;

    public MetadataCollection<(string name, int value)> CsEnumValues =>
        csEnumValues ?? MetadataCollection<(string name, int value)>.Empty;

    public MetadataCollection<(string name, int value)> CsValuesOrDbValues =>
        CsEnumValues.Count > 0 ? CsEnumValues : DbEnumValues;

    public MetadataCollection<(string name, int value)> DbValuesOrCsValues =>
        DbEnumValues.Count > 0 ? DbEnumValues : CsEnumValues;

    public bool DeclaredInClass { get; }
}

public class RelationProperty : PropertyDefinition
{
    public RelationPart RelationPart { get; private set; } = null!;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetRelationPart(RelationPart relationPart)
    {
        SetRelationPartCore(relationPart);
    }

    internal void SetRelationPartCore(RelationPart relationPart)
    {
        ThrowIfFrozen();
        RelationPart = relationPart;
    }

    public string? RelationName { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetRelationName(string? relationName)
    {
        SetRelationNameCore(relationName);
    }

    internal void SetRelationNameCore(string? relationName)
    {
        ThrowIfFrozen();
        RelationName = relationName;
    }

    public RelationProperty(string propertyName, CsTypeDeclaration csType, ModelDefinition model, IEnumerable<Attribute> attributes) : base(propertyName, csType, model, attributes)
    {
        Type = PropertyType.Relation;

        // Find the RelationAttribute among the provided attributes and set the RelationName.
        var relationAttribute = attributes.OfType<RelationAttribute>().FirstOrDefault();
        if (relationAttribute?.Name != null)
        {
            this.RelationName = relationAttribute.Name;
        }
    }
}
