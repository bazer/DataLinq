using System;

namespace DataLinq.Metadata;

public readonly struct GeneratedTableModelDeclaration
{
    public GeneratedTableModelDeclaration(string csPropertyName, Type modelType)
    {
        CsPropertyName = csPropertyName;
        ModelType = modelType;
    }

    public string CsPropertyName { get; }
    public Type ModelType { get; }
}
