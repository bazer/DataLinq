using System;

namespace DataLinq.Metadata;

public enum ScalarConverterOrigin
{
    None,
    Property,
    AssemblyRegistration
}

public sealed class ColumnScalarMapping
{
    private ColumnScalarMapping(
        CsTypeDeclaration modelCsType,
        CsTypeDeclaration providerCsType,
        CsTypeDeclaration? converterCsType,
        IDataLinqScalarConverter? converter,
        ScalarConverterOrigin origin,
        SourceLocation? sourceLocation)
    {
        ModelCsType = modelCsType;
        ProviderCsType = providerCsType;
        ConverterCsType = converterCsType;
        Converter = converter;
        Origin = origin;
        SourceLocation = sourceLocation;
    }

    public CsTypeDeclaration ModelCsType { get; }
    public CsTypeDeclaration ProviderCsType { get; }
    public CsTypeDeclaration? ConverterCsType { get; }
    public Type? ModelClrType => ModelCsType.Type;
    public Type? ProviderClrType => ProviderCsType.Type;
    public Type? ConverterClrType => ConverterCsType?.Type;
    public IDataLinqScalarConverter? Converter { get; }
    public ScalarConverterOrigin Origin { get; }
    public SourceLocation? SourceLocation { get; }
    public bool HasConverter => ConverterCsType.HasValue;
    public bool IsConverterResolved => !HasConverter || Converter is not null;

    internal static ColumnScalarMapping Identity(CsTypeDeclaration modelCsType) =>
        new(modelCsType, modelCsType, null, null, ScalarConverterOrigin.None, null);

    internal static ColumnScalarMapping Converted(
        CsTypeDeclaration modelCsType,
        CsTypeDeclaration providerCsType,
        CsTypeDeclaration converterCsType,
        IDataLinqScalarConverter? converter,
        ScalarConverterOrigin origin,
        SourceLocation? sourceLocation = null) =>
        new(modelCsType, providerCsType, converterCsType, converter, origin, sourceLocation);
}
