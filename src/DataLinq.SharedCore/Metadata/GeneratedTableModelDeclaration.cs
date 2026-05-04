using System;
using System.Diagnostics.CodeAnalysis;

namespace DataLinq.Metadata;

public readonly struct GeneratedDatabaseModelDeclaration
{
    public GeneratedDatabaseModelDeclaration(params GeneratedTableModelDeclaration[] tableModels)
    {
        TableModels = tableModels ?? throw new ArgumentNullException(nameof(tableModels));
    }

    public GeneratedTableModelDeclaration[] TableModels { get; }
}

public readonly struct GeneratedTableModelDeclaration
{
    public GeneratedTableModelDeclaration(
        string csPropertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)]
        Type modelType)
        : this(csPropertyName, modelType, immutableType: null, mutableType: null, immutableFactory: null, tableType: TableType.Table)
    {
    }

    public GeneratedTableModelDeclaration(
        string csPropertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)]
        Type modelType,
        Type? immutableType,
        Type? mutableType,
        Delegate? immutableFactory)
        : this(csPropertyName, modelType, immutableType, mutableType, immutableFactory, TableType.Table)
    {
    }

    public GeneratedTableModelDeclaration(
        string csPropertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)]
        Type modelType,
        Type? immutableType,
        Type? mutableType,
        Delegate? immutableFactory,
        TableType tableType)
    {
        CsPropertyName = csPropertyName;
        ModelType = modelType;
        ImmutableType = immutableType;
        MutableType = mutableType;
        ImmutableFactory = immutableFactory;
        TableType = tableType;
    }

    public string CsPropertyName { get; }
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)]
    public Type ModelType { get; }
    public Type? ImmutableType { get; }
    public Type? MutableType { get; }
    public Delegate? ImmutableFactory { get; }
    public TableType TableType { get; }
}
