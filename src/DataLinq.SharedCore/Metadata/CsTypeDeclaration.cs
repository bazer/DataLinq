using System;
using DataLinq.Core.Factories;

namespace DataLinq.Metadata;

public enum ModelCsType
{
    Class,
    Record,
    Interface,
    Struct,
    Primitive,
    Enum,
    Tuple,
    Pointer
}

public class ModelUsing(string fullNamespaceName)
{
    public string FullNamespaceName { get; } = fullNamespaceName;

    public override string ToString() => FullNamespaceName;
}

public readonly partial record struct CsTypeDeclaration
{
    public readonly Type? Type { get; }
    public readonly string Name { get; }
    public readonly string Namespace { get; }
    public readonly ModelCsType ModelCsType { get; }

    public CsTypeDeclaration(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        Type = type;
        Name = MetadataTypeConverter.GetKeywordName(type);
        Namespace = type.Namespace ?? string.Empty;
        ModelCsType = ParseModelCsType(type);
    }

    public CsTypeDeclaration(string name, string @namespace, ModelCsType modelCsType)
    {
        Name = name;
        Namespace = @namespace;
        ModelCsType = modelCsType;
    }

    public CsTypeDeclaration MutateName(string name) => new(name, Namespace, ModelCsType);
    public CsTypeDeclaration MutateNamespace(string @namespace) => new(Name, @namespace, ModelCsType);

    public static ModelCsType ParseModelCsType(Type type)
    {
        if (type.IsClass)
            return ModelCsType.Class;

        if (type.IsInterface)
            return ModelCsType.Interface;

        if (type.IsValueType)
        {
            if (type.IsPrimitive)
            {
                // Handle primitive types (e.g., int, bool, char)
                return ModelCsType.Primitive;
            }
            else
            {
                // Handle other value types (e.g., structs, enums)
                return ModelCsType.Struct;
            }
        }

        throw new NotImplementedException($"Unknown type '{type}'");
    }

}
