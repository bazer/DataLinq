using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DataLinq.ErrorHandling;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Metadata;

public readonly struct GeneratedDatabaseModelDeclaration
{
    private readonly GeneratedTableModelDeclaration[]? tableModels;

    public GeneratedDatabaseModelDeclaration(params GeneratedTableModelDeclaration[] tableModels)
    {
        this.tableModels = tableModels?.ToArray() ?? throw new ArgumentNullException(nameof(tableModels));
    }

    public GeneratedTableModelDeclaration[] TableModels => tableModels?.ToArray() ?? [];

    public Option<bool, IDLOptionFailure> TryValidate(Type databaseType)
    {
        if (databaseType is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Database type cannot be null.");

        if (tableModels is null)
            return MissingMember(GetDiagnosticDescription(databaseType), nameof(TableModels));

        foreach (var tableModel in tableModels)
        {
            if (!tableModel.TryValidate(databaseType).TryUnwrap(out _, out var failure))
                return failure;
        }

        return true;
    }

    public void Validate(Type databaseType)
    {
        if (!TryValidate(databaseType).TryUnwrap(out _, out var failure))
            throw new InvalidOperationException(failure.ToString());
    }

    private static string GetDiagnosticDescription(Type databaseType) =>
        $"Generated DataLinq metadata declaration for database '{databaseType.FullName}'";

    private static IDLOptionFailure MissingMember(string description, string memberName) =>
        DLOptionFailure.Fail(DLFailureType.InvalidModel, $"{description} is missing required member '{memberName}'.");
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

    public Option<bool, IDLOptionFailure> TryValidate(Type databaseType)
    {
        if (databaseType is null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Database type cannot be null.");

        var description = GetDiagnosticDescription(databaseType);

        if (string.IsNullOrWhiteSpace(CsPropertyName))
            return MissingMember(description, nameof(CsPropertyName));

        if (ModelType is null)
            return MissingMember(description, nameof(ModelType));

        if (ImmutableType is null)
            return MissingMember(description, nameof(ImmutableType));

        if (TableType == TableType.Table && MutableType is null)
            return MissingMember(description, nameof(MutableType));

        if (ImmutableFactory is null)
            return MissingMember(description, nameof(ImmutableFactory));

        if (!Enum.IsDefined(typeof(TableType), TableType))
            return MalformedMember(description, nameof(TableType), $"Defined {nameof(TableType)} value", TableType.ToString());

        if (!HasExpectedImmutableFactoryShape(ImmutableFactory))
            return MalformedMember(description, nameof(ImmutableFactory), ExpectedImmutableFactoryTypeName, ImmutableFactory.GetType().FullName);

        return true;
    }

    public void Validate(Type databaseType)
    {
        if (!TryValidate(databaseType).TryUnwrap(out _, out var failure))
            throw new InvalidOperationException(failure.ToString());
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

    private static IDLOptionFailure MissingMember(string description, string memberName) =>
        DLOptionFailure.Fail(DLFailureType.InvalidModel, $"{description} is missing required member '{memberName}'.");

    private static IDLOptionFailure MalformedMember(string description, string memberName, string expected, string? actual) =>
        DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            $"{description} has malformed member '{memberName}'. Expected '{expected}'; actual '{actual}'.");
}
