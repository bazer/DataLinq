using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Metadata;

public enum PropertyType
{
    Value,
    Relation
}

public abstract class PropertyDefinition(string propertyName, CsTypeDeclaration csType, ModelDefinition model, IEnumerable<Attribute> attributes) : IDefinition
{
    public Attribute[] Attributes { get; private set; } = attributes.ToArray();
    public void SetAttributes(IEnumerable<Attribute> attributes) => Attributes = attributes.ToArray();
    public void AddAttribute(Attribute attribute) => Attributes = [.. Attributes, attribute];
    public string PropertyName { get; private set; } = propertyName;
    public void SetPropertyName(string propertyName) => PropertyName = propertyName;
    public CsTypeDeclaration CsType { get; private set; } = csType;
    public void SetCsType(CsTypeDeclaration csType) => CsType = csType;
    public bool CsNullable { get; private set; }
    public void SetCsNullable(bool csNullable = true) => CsNullable = csNullable;
    public ModelDefinition Model { get; private set; } = model;
    public PropertyType Type { get; protected private set; }
    public PropertySourceInfo? SourceInfo { get; private set; }
    public void SetSourceInfo(PropertySourceInfo sourceInfo) => SourceInfo = sourceInfo;

    public CsFileDeclaration? CsFile => Model?.CsFile;

    public override string ToString() => $"Property: {CsType.Name} {PropertyName}";
}

public class ValueProperty : PropertyDefinition
{
    public ColumnDefinition Column { get; private set; }
    public void SetColumn(ColumnDefinition column) => Column = column;
    public int? CsSize { get; private set; }
    public void SetCsSize(int? csSize) => CsSize = csSize;
    public EnumProperty? EnumProperty { get; private set; }
    public void SetEnumProperty(EnumProperty enumProperty) => EnumProperty = enumProperty;

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

        return defaultAttr?.Value.ToString();
    }

    public string? GetDefaultValueCode()
    {
        var defaultAttr = GetDefaultAttribute();

        if (defaultAttr is DefaultCurrentTimestampAttribute || defaultAttr is DefaultNewUUIDAttribute)
            return GetDefaultValue();

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
            "string" => SymbolDisplay.FormatLiteral(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, quote: true),
            "char" => SymbolDisplay.FormatLiteral(Convert.ToChar(value, CultureInfo.InvariantCulture), quote: true),
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
            "DateTime" => $"DateTime.Parse({SymbolDisplay.FormatLiteral(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), quote: true)})",
            "DateTimeOffset" => $"DateTimeOffset.Parse({SymbolDisplay.FormatLiteral(((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), quote: true)})",
            "TimeSpan" => $"TimeSpan.Parse({SymbolDisplay.FormatLiteral(((TimeSpan)value).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture), quote: true)})",
            "DateOnly" => $"DateOnly.Parse({SymbolDisplay.FormatLiteral(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, quote: true)})",
            "TimeOnly" => $"TimeOnly.Parse({SymbolDisplay.FormatLiteral(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, quote: true)})",
            "Guid" or "System.Guid" => $"Guid.Parse({SymbolDisplay.FormatLiteral(((Guid)value).ToString(), quote: true)})",
            _ => value.ToString() ?? string.Empty,
        };
    }
}

public record struct EnumProperty
{
    public EnumProperty(IEnumerable<(string name, int value)>? enumValues = null, IEnumerable<(string name, int value)>? csEnumValues = null, bool declaredInClass = true)
    {
        DbEnumValues = enumValues?.ToList() ?? [];
        CsEnumValues = csEnumValues?.ToList() ?? [];
        DeclaredInClass = declaredInClass;
    }

    public List<(string name, int value)> DbEnumValues { get; }
    public List<(string name, int value)> CsEnumValues { get; }
    public List<(string name, int value)> CsValuesOrDbValues => CsEnumValues.Count != 0 ? CsEnumValues : DbEnumValues;
    public List<(string name, int value)> DbValuesOrCsValues => DbEnumValues.Count != 0 ? DbEnumValues : CsEnumValues;
    public bool DeclaredInClass { get; }
}

public class RelationProperty : PropertyDefinition
{
    public RelationPart RelationPart { get; private set; }
    public void SetRelationPart(RelationPart relationPart) => RelationPart = relationPart;
    public string? RelationName { get; private set; }
    public void SetRelationName(string? relationName) => RelationName = relationName;

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
