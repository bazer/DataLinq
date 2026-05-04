using System;

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
    public GeneratedTableModelDeclaration(string csPropertyName, Type modelType)
        : this(csPropertyName, modelType, immutableType: null, mutableType: null, immutableFactory: null)
    {
    }

    public GeneratedTableModelDeclaration(
        string csPropertyName,
        Type modelType,
        Type? immutableType,
        Type? mutableType,
        Delegate? immutableFactory)
    {
        CsPropertyName = csPropertyName;
        ModelType = modelType;
        ImmutableType = immutableType;
        MutableType = mutableType;
        ImmutableFactory = immutableFactory;
    }

    public string CsPropertyName { get; }
    public Type ModelType { get; }
    public Type? ImmutableType { get; }
    public Type? MutableType { get; }
    public Delegate? ImmutableFactory { get; }
}
