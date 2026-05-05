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

    public void Validate(Type databaseType)
    {
        if (databaseType is null)
            throw new ArgumentNullException(nameof(databaseType));

        if (TableModels is null)
        {
            throw new InvalidOperationException(
                $"Generated DataLinq metadata declaration for database '{databaseType.FullName}' is missing required member '{nameof(TableModels)}'.");
        }

        foreach (var tableModel in TableModels)
            tableModel.Validate(databaseType);
    }
}

public readonly struct GeneratedTableModelDeclaration
{
    private const string ImmutableFactoryRowDataTypeName = "DataLinq.Instances.IRowData";
    private const string ImmutableFactoryDataSourceTypeName = "DataLinq.Interfaces.IDataSourceAccess";
    private const string ImmutableFactoryReturnTypeName = "DataLinq.Instances.IImmutableInstance";

    public GeneratedTableModelDeclaration(
        string csPropertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)]
        Type modelType,
        Type immutableType,
        Type? mutableType,
        Delegate immutableFactory,
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

    public void Validate(Type databaseType)
    {
        var description = GetDiagnosticDescription(databaseType);

        if (string.IsNullOrWhiteSpace(CsPropertyName))
            throw MissingMember(description, nameof(CsPropertyName));

        if (ModelType is null)
            throw MissingMember(description, nameof(ModelType));

        if (ImmutableType is null)
            throw MissingMember(description, nameof(ImmutableType));

        if (TableType == TableType.Table && MutableType is null)
            throw MissingMember(description, nameof(MutableType));

        if (ImmutableFactory is null)
            throw MissingMember(description, nameof(ImmutableFactory));

        if (!HasExpectedImmutableFactoryShape(ImmutableFactory))
        {
            throw new InvalidOperationException(
                $"{description} has malformed member '{nameof(ImmutableFactory)}'. " +
                $"Expected '{ExpectedImmutableFactoryTypeName}'; actual '{ImmutableFactory.GetType().FullName}'.");
        }
    }

    private static string ExpectedImmutableFactoryTypeName =>
        $"System.Func<{ImmutableFactoryRowDataTypeName}, {ImmutableFactoryDataSourceTypeName}, {ImmutableFactoryReturnTypeName}>";

    private static bool HasExpectedImmutableFactoryShape(Delegate immutableFactory)
    {
        var factoryType = immutableFactory.GetType();
        if (!factoryType.IsGenericType || factoryType.GetGenericTypeDefinition() != typeof(Func<,,>))
            return false;

        var genericArguments = factoryType.GetGenericArguments();
        return genericArguments.Length == 3 &&
            genericArguments[0].FullName == ImmutableFactoryRowDataTypeName &&
            genericArguments[1].FullName == ImmutableFactoryDataSourceTypeName &&
            genericArguments[2].FullName == ImmutableFactoryReturnTypeName;
    }

    private string GetDiagnosticDescription(Type databaseType)
    {
        var databaseTypeName = databaseType?.FullName ?? "<unknown database>";
        var modelTypeName = ModelType?.FullName ?? "<unknown model>";
        var propertyName = string.IsNullOrWhiteSpace(CsPropertyName) ? "<unknown property>" : CsPropertyName;

        return $"Generated DataLinq metadata declaration for database '{databaseTypeName}', {TableType} model '{modelTypeName}' (database property '{propertyName}')";
    }

    private static InvalidOperationException MissingMember(string description, string memberName) =>
        new($"{description} is missing required member '{memberName}'.");
}
