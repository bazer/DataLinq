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
    public bool SupportsScalarProviderKeyStore => IsScalar && components[0].StoreKind != TableKeyComponentStoreKind.Unsupported;

    public TableKeyComponentDefinition this[int index] => components[index];

    public bool SupportsScalarProviderKey(Type keyType)
    {
        if (!SupportsScalarProviderKeyStore)
            return false;

        return GetStoreKind(keyType) == components[0].StoreKind;
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
}

public sealed class TableKeyComponentDefinition
{
    private TableKeyComponentDefinition(
        ColumnDefinition column,
        int keyOrdinal,
        Type? providerClrType,
        Type? modelClrType,
        bool nullable,
        TableKeyComponentStoreKind storeKind)
    {
        Column = column;
        KeyOrdinal = keyOrdinal;
        ColumnOrdinal = column.Index;
        ProviderClrType = providerClrType;
        ModelClrType = modelClrType;
        Nullable = nullable;
        StoreKind = storeKind;
    }

    public ColumnDefinition Column { get; }
    public int KeyOrdinal { get; }
    public int ColumnOrdinal { get; }
    public Type? ProviderClrType { get; }
    public Type? ModelClrType { get; }
    public bool Nullable { get; }
    public object? ScalarConverterHandle => null;
    public TableKeyComponentStoreKind StoreKind { get; }

    internal static TableKeyComponentDefinition Create(ColumnDefinition column, int keyOrdinal)
    {
        var property = column.ValueProperty;
        var modelClrType = property.CsType.Type;

        return new TableKeyComponentDefinition(
            column,
            keyOrdinal,
            providerClrType: modelClrType,
            modelClrType: modelClrType,
            nullable: column.Nullable || property.CsNullable,
            storeKind: TableKeyShape.GetStoreKind(property.CsType));
    }
}
