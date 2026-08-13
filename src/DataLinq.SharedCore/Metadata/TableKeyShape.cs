using System;
using System.Collections.Generic;

namespace DataLinq.Metadata;

public enum TableKeyComponentStoreKind
{
    Unsupported,
    Int32,
    Int64,
    Guid,
    String
}

public sealed class TableKeyShape
{
    public static readonly TableKeyShape Empty = new([]);

    private readonly MetadataCollection<TableKeyComponentDefinition> components;

    private TableKeyShape(IEnumerable<TableKeyComponentDefinition> components)
    {
        this.components = new MetadataCollection<TableKeyComponentDefinition>(components);
    }

    public MetadataCollection<TableKeyComponentDefinition> Components => components;
    public int Arity => components.Count;
    public bool IsScalar => Arity == 1;
    public bool IsComposite => Arity > 1;
    public bool SupportsScalarProviderKeyStore => IsScalar && components[0].ProviderStoreKind != TableKeyComponentStoreKind.Unsupported;
    public bool HasScalarConverter
    {
        get
        {
            foreach (var component in components)
            {
                if (component.HasScalarConverter)
                    return true;
            }

            return false;
        }
    }

    public TableKeyComponentDefinition this[int index] => components[index];

    public bool SupportsScalarProviderKey(Type keyType)
    {
        if (!SupportsScalarProviderKeyStore)
            return false;

        return GetStoreKind(keyType) == components[0].ProviderStoreKind;
    }

    internal static TableKeyShape Create(IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 0)
            return Empty;

        var components = new TableKeyComponentDefinition[columns.Count];
        for (var i = 0; i < columns.Count; i++)
            components[i] = TableKeyComponentDefinition.Create(columns[i], i);

        return new TableKeyShape(components);
    }

    internal static TableKeyComponentStoreKind GetStoreKind(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(int))
            return TableKeyComponentStoreKind.Int32;

        if (type == typeof(long))
            return TableKeyComponentStoreKind.Int64;

        if (type == typeof(Guid))
            return TableKeyComponentStoreKind.Guid;

        if (type == typeof(string))
            return TableKeyComponentStoreKind.String;

        return TableKeyComponentStoreKind.Unsupported;
    }

    internal static TableKeyComponentStoreKind GetStoreKind(CsTypeDeclaration type)
    {
        if (type.Type is not null)
            return GetStoreKind(type.Type);

        return type.Name switch
        {
            "int" or "Int32" or "System.Int32" => TableKeyComponentStoreKind.Int32,
            "long" or "Int64" or "System.Int64" => TableKeyComponentStoreKind.Int64,
            "Guid" or "System.Guid" => TableKeyComponentStoreKind.Guid,
            "string" or "String" or "System.String" => TableKeyComponentStoreKind.String,
            _ => TableKeyComponentStoreKind.Unsupported
        };
    }

    internal static CsTypeDeclaration GetProviderCsType(ColumnDefinition column)
    {
        return column.ProviderCsType;
    }

    internal static TableKeyComponentStoreKind GetProviderStoreKind(ColumnDefinition column)
    {
        // SC-1 records the canonical provider type, but canonical key normalization
        // does not arrive until SC-3. Keep converted components off every current
        // model-valued fast path until that normalization boundary exists.
        return column.HasScalarConverter
            ? TableKeyComponentStoreKind.Unsupported
            : GetStoreKind(GetProviderCsType(column));
    }

    internal static object? GetScalarConverterHandle(ColumnDefinition column) =>
        column.HasScalarConverter
            ? (object?)column.ScalarConverter ?? column.ScalarMapping
            : null;
}

public sealed class TableKeyComponentDefinition
{
    private TableKeyComponentDefinition(
        ColumnDefinition column,
        int keyOrdinal,
        Type? providerClrType,
        Type? modelClrType,
        CsTypeDeclaration providerCsType,
        CsTypeDeclaration modelCsType,
        object? scalarConverterHandle,
        bool nullable,
        TableKeyComponentStoreKind providerStoreKind)
    {
        Column = column;
        KeyOrdinal = keyOrdinal;
        ColumnOrdinal = column.Index;
        ProviderClrType = providerClrType;
        ModelClrType = modelClrType;
        ProviderCsType = providerCsType;
        ModelCsType = modelCsType;
        ScalarConverterHandle = scalarConverterHandle;
        Nullable = nullable;
        ProviderStoreKind = providerStoreKind;
    }

    public ColumnDefinition Column { get; }
    public int KeyOrdinal { get; }
    public int ColumnOrdinal { get; }
    public Type? ProviderClrType { get; }
    public Type? ModelClrType { get; }
    public CsTypeDeclaration ProviderCsType { get; }
    public CsTypeDeclaration ModelCsType { get; }
    public bool Nullable { get; }
    public object? ScalarConverterHandle { get; }
    public bool HasScalarConverter => ScalarConverterHandle is not null;
    public TableKeyComponentStoreKind ProviderStoreKind { get; }

    internal static TableKeyComponentDefinition Create(ColumnDefinition column, int keyOrdinal)
    {
        var property = column.ValueProperty;
        var modelCsType = property.CsType;
        var providerCsType = TableKeyShape.GetProviderCsType(column);

        return new TableKeyComponentDefinition(
            column,
            keyOrdinal,
            providerClrType: providerCsType.Type,
            modelClrType: modelCsType.Type,
            providerCsType: providerCsType,
            modelCsType: modelCsType,
            scalarConverterHandle: TableKeyShape.GetScalarConverterHandle(column),
            nullable: column.Nullable || property.CsNullable,
            providerStoreKind: TableKeyShape.GetProviderStoreKind(column));
    }
}
