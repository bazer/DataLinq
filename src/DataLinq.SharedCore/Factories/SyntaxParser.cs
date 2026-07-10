using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories;

public class SyntaxParser
{
    private readonly ImmutableArray<TypeDeclarationSyntax> modelSyntaxes;
    private readonly ImmutableArray<EnumDeclarationSyntax> enumSyntaxes;

    public static bool IsModelInterface(string interfaceName) =>
        IsDatabaseModelContract(interfaceName) ||
        IsTableModelContract(interfaceName) ||
        IsViewModelContract(interfaceName) ||
        IsModelInstanceContract(interfaceName);

    public static bool IsModelInterface(TypeSyntax interfaceType) =>
        IsModelInterface(GetUnqualifiedTypeName(interfaceType));

    internal static bool IsTableModelContract(string interfaceName) =>
        MatchesModelInterfaceContract(interfaceName, "ITableModel", allowSingleGenericArgument: true);

    internal static bool IsViewModelContract(string interfaceName) =>
        MatchesModelInterfaceContract(interfaceName, "IViewModel", allowSingleGenericArgument: true);

    internal static bool IsTableOrViewModelContractForDatabase(string interfaceName, string databaseTypeName) =>
        MatchesModelInterfaceContractForDatabase(interfaceName, "ITableModel", databaseTypeName) ||
        MatchesModelInterfaceContractForDatabase(interfaceName, "IViewModel", databaseTypeName);

    internal static bool TryGetInvalidModelInterfaceContractArity(
        string interfaceName,
        out string contractName,
        out int typeArgumentCount,
        out string expectedDescription)
    {
        contractName = string.Empty;
        typeArgumentCount = 0;
        expectedDescription = string.Empty;

        var unqualifiedTypeName = GetUnqualifiedTypeName(interfaceName);
        if (!TrySplitGenericTypeName(unqualifiedTypeName, out var genericName, out var typeArguments))
            return false;

        if (string.Equals(genericName, "IDatabaseModel", StringComparison.Ordinal))
        {
            if (typeArguments.Count == 1)
                return false;

            contractName = genericName;
            typeArgumentCount = typeArguments.Count;
            expectedDescription = "must be non-generic or use exactly one database type argument";
            return true;
        }

        if ((string.Equals(genericName, "ITableModel", StringComparison.Ordinal) ||
            string.Equals(genericName, "IViewModel", StringComparison.Ordinal) ||
            string.Equals(genericName, "IModelInstance", StringComparison.Ordinal)) &&
            typeArguments.Count != 1)
        {
            contractName = genericName;
            typeArgumentCount = typeArguments.Count;
            expectedDescription = "must be non-generic or use exactly one database type argument";
            return true;
        }

        return false;
    }

    internal static bool IsDatabaseModelContract(string interfaceName) =>
        MatchesModelInterfaceContract(interfaceName, "IDatabaseModel", allowSingleGenericArgument: true);

    private static bool IsModelInstanceContract(string interfaceName) =>
        MatchesModelInterfaceContract(interfaceName, "IModelInstance", allowSingleGenericArgument: true);

    private static bool MatchesModelInterfaceContract(
        string interfaceName,
        string expectedTypeName,
        bool allowSingleGenericArgument)
    {
        var unqualifiedTypeName = GetUnqualifiedTypeName(interfaceName);
        if (string.Equals(unqualifiedTypeName, expectedTypeName, StringComparison.Ordinal))
            return true;

        return allowSingleGenericArgument &&
            TrySplitGenericTypeName(unqualifiedTypeName, out var genericName, out var typeArguments) &&
            string.Equals(genericName, expectedTypeName, StringComparison.Ordinal) &&
            typeArguments.Count == 1;
    }

    private static bool MatchesModelInterfaceContractForDatabase(
        string interfaceName,
        string expectedTypeName,
        string databaseTypeName)
    {
        var unqualifiedTypeName = GetUnqualifiedTypeName(interfaceName);
        if (!TrySplitGenericTypeName(unqualifiedTypeName, out var genericName, out var typeArguments) ||
            !string.Equals(genericName, expectedTypeName, StringComparison.Ordinal) ||
            typeArguments.Count != 1)
            return false;

        var contractDatabaseName = GetUnqualifiedTypeName(typeArguments[0]);
        var expectedDatabaseName = GetUnqualifiedTypeName(databaseTypeName);
        return string.Equals(contractDatabaseName, expectedDatabaseName, StringComparison.Ordinal);
    }

    private static bool TrySplitGenericTypeName(
        string typeName,
        out string genericName,
        out IReadOnlyList<string> typeArguments)
    {
        genericName = string.Empty;
        typeArguments = [];

        var genericStart = typeName.IndexOf('<');
        if (genericStart < 0 || !typeName.EndsWith(">", StringComparison.Ordinal))
            return false;

        genericName = typeName.Substring(0, genericStart).Trim();
        var argumentsText = typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2);
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            typeArguments = arguments;
            return true;
        }

        var depth = 0;
        var argumentStart = 0;
        for (var i = 0; i < argumentsText.Length; i++)
        {
            if (argumentsText[i] == '<')
                depth++;
            else if (argumentsText[i] == '>')
                depth--;
            else if (argumentsText[i] == ',' && depth == 0)
            {
                arguments.Add(argumentsText.Substring(argumentStart, i - argumentStart).Trim());
                argumentStart = i + 1;
            }

            if (depth < 0)
                return false;
        }

        if (depth != 0)
            return false;

        arguments.Add(argumentsText.Substring(argumentStart).Trim());
        typeArguments = arguments;
        return true;
    }

    internal static bool MatchesUnqualifiedTypeName(string typeName, string expectedTypeName)
    {
        var unqualifiedTypeName = GetUnqualifiedTypeName(typeName);
        return string.Equals(unqualifiedTypeName, expectedTypeName, StringComparison.Ordinal) ||
            unqualifiedTypeName.StartsWith(expectedTypeName + "<", StringComparison.Ordinal);
    }

    internal static string ApplyTypeParameterSubstitutions(
        string typeName,
        IReadOnlyDictionary<string, string> typeParameterSubstitutions)
    {
        var unqualifiedTypeName = GetUnqualifiedTypeName(typeName);
        if (typeParameterSubstitutions.Count == 0)
            return unqualifiedTypeName;

        if (typeParameterSubstitutions.TryGetValue(unqualifiedTypeName, out var substitutedTypeName))
            return substitutedTypeName;

        if (!TrySplitGenericTypeName(unqualifiedTypeName, out var genericName, out var typeArguments))
            return unqualifiedTypeName;

        var substitutedTypeArguments = typeArguments
            .Select(typeArgument => ApplyTypeParameterSubstitutions(typeArgument, typeParameterSubstitutions))
            .ToJoinedString(", ");

        return $"{genericName}<{substitutedTypeArguments}>";
    }

    internal static Dictionary<string, string> CreateTypeParameterSubstitutions(
        InterfaceDeclarationSyntax interfaceDeclaration,
        string interfaceTypeName,
        IReadOnlyDictionary<string, string> inheritedSubstitutions)
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var substitution in inheritedSubstitutions)
            substitutions[substitution.Key] = substitution.Value;

        var typeParameters = interfaceDeclaration.TypeParameterList?.Parameters
            .Select(parameter => parameter.Identifier.Text)
            .ToArray() ?? [];

        if (typeParameters.Length == 0)
            return substitutions;

        var unqualifiedTypeName = GetUnqualifiedTypeName(interfaceTypeName);
        if (!TrySplitGenericTypeName(unqualifiedTypeName, out _, out var typeArguments) ||
            typeArguments.Count != typeParameters.Length)
            return substitutions;

        for (var i = 0; i < typeParameters.Length; i++)
        {
            substitutions[typeParameters[i]] = ApplyTypeParameterSubstitutions(
                typeArguments[i],
                inheritedSubstitutions);
        }

        return substitutions;
    }

    internal static string GetUnqualifiedTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            GenericNameSyntax genericName => FormatGenericName(genericName),
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualifiedName => GetUnqualifiedTypeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => GetUnqualifiedTypeName(aliasQualifiedName.Name),
            NullableTypeSyntax nullableType => $"{GetUnqualifiedTypeName(nullableType.ElementType)}?",
            ArrayTypeSyntax arrayType => $"{GetUnqualifiedTypeName(arrayType.ElementType)}{string.Concat(arrayType.RankSpecifiers.Select(x => x.ToString()))}",
            PredefinedTypeSyntax predefinedType => predefinedType.Keyword.Text,
            _ => typeSyntax.ToString()
        };
    }

    private static string FormatGenericName(GenericNameSyntax genericName)
    {
        var typeArguments = genericName.TypeArgumentList.Arguments
            .Select(GetUnqualifiedTypeName)
            .ToJoinedString(", ");

        return $"{genericName.Identifier.Text}<{typeArguments}>";
    }

    private static string GetUnqualifiedTypeName(string typeName)
    {
        var trimmedTypeName = typeName.Trim();
        var genericStart = trimmedTypeName.IndexOf('<');
        var prefix = genericStart >= 0
            ? trimmedTypeName.Substring(0, genericStart)
            : trimmedTypeName;
        var suffix = genericStart >= 0
            ? trimmedTypeName.Substring(genericStart)
            : string.Empty;

        var dotIndex = prefix.LastIndexOf('.');
        var aliasIndex = LastAliasSeparatorIndex(prefix);
        var separatorIndex = Math.Max(dotIndex, aliasIndex);

        return separatorIndex >= 0
            ? prefix.Substring(separatorIndex + (prefix[separatorIndex] == ':' ? 2 : 1)) + suffix
            : trimmedTypeName;
    }

    private static int LastAliasSeparatorIndex(string text)
    {
        for (var i = text.Length - 2; i >= 0; i--)
        {
            if (text[i] == ':' && text[i + 1] == ':')
                return i;
        }

        return -1;
    }
    
    public SyntaxParser(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes, ImmutableArray<EnumDeclarationSyntax> enumSyntaxes = default)
    {
        this.modelSyntaxes = modelSyntaxes;
        this.enumSyntaxes = enumSyntaxes.IsDefault ? [] : enumSyntaxes;
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public Option<TableModel, IDLOptionFailure> ParseTableModel(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        return ParseTableModelCore(database, typeSyntax, csPropertyName);
    }

    internal Option<TableModel, IDLOptionFailure> ParseTableModelCore(DatabaseDefinition database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        ModelDefinition model;
        if (typeSyntax == null)
        {
            model = new ModelDefinition(new CsTypeDeclaration(csPropertyName, database.CsType.Namespace, ModelCsType.Interface));
            model.SetInterfacesCore([new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        }
        else
        {
            if (!ParseModel(typeSyntax).TryUnwrap(out model, out var failure))
                return failure;
        }

        return TableModel.FromParsedModelCore(csPropertyName, database, model, typeSyntax == null);
    }

    public Option<MetadataTableModelDraft, IDLOptionFailure> ParseTableModelDraft(
        CsTypeDeclaration databaseCsType,
        TypeDeclarationSyntax? typeSyntax,
        string csPropertyName)
    {
        MetadataModelDraft model;
        if (typeSyntax == null)
        {
            model = new MetadataModelDraft(new CsTypeDeclaration(csPropertyName, databaseCsType.Namespace, ModelCsType.Interface))
            {
                OriginalInterfaces = [new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]
            };
        }
        else
        {
            if (!ParseModelDraft(typeSyntax).TryUnwrap(out model, out var failure))
                return failure;
        }

        if (!ParseTableDraft(model).TryUnwrap(out var table, out var tableFailure))
            return tableFailure;

        return new MetadataTableModelDraft(csPropertyName, model, table)
        {
            IsStub = typeSyntax == null
        };
    }

    private Option<ModelDefinition, IDLOptionFailure> ParseModel(TypeDeclarationSyntax typeSyntax)
    {
        var model = new ModelDefinition(CsTypeDeclarationSyntax.Create(typeSyntax));
        model.SetSourceSpanCore(new SourceTextSpan(typeSyntax.SpanStart, typeSyntax.Span.Length));

        if (!string.IsNullOrEmpty(typeSyntax.SyntaxTree.FilePath))
            model.SetCsFileCore(new CsFileDeclaration(typeSyntax.SyntaxTree.FilePath));

        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes", model, failures);

        model.SetAttributesCore(parsedAttributes);
        foreach (var (attribute, sourceSpan) in attributeSourceSpans)
            model.SetAttributeSourceSpanCore(attribute, sourceSpan);

        var modelInstanceInterfaces = model.Attributes
            .Where(x => x is InterfaceAttribute interfaceAttribute && interfaceAttribute.GenerateInterface)
            .Select(x => x as InterfaceAttribute)
            .Select(x => new CsTypeDeclaration(x?.Name ?? $"I{model.CsType.Name}", model.CsType.Namespace, ModelCsType.Interface))
            .ToList();

        if (modelInstanceInterfaces.Count > 1)
            return DLOptionFailure.Fail(DLFailureType.InvalidArgument,
                $"Multiple model instance interfaces ({modelInstanceInterfaces.Select(x => x.Name).ToJoinedString(", ")}) found in model", model);

        //if (modelInstanceInterfaces.GroupBy(x => x.Name).Any(x => x.Count() > 1))
        //    return DLOptionFailure.Fail(DLFailureType.InvalidArgument,
        //        $"Duplicate interface names {modelInstanceInterfaces.GroupBy(x => x.Name).Where(x => x.Count() > 1).ToJoinedString()} in model '{model.CsType.Name}'");

        if (modelInstanceInterfaces.Any())
            model.SetModelInstanceInterfaceCore(modelInstanceInterfaces.First());

        if (typeSyntax.BaseList != null)
        {
            if (!ParseDeclaredInterfaces(typeSyntax).TryUnwrap(out var declaredInterfaces, out var interfaceFailure))
                return DLOptionFailure.Fail($"Parsing base interfaces for {typeSyntax.Identifier.Text}", model, [interfaceFailure]);

            model.SetInterfacesCore(declaredInterfaces
                .Where(x => !MatchesUnqualifiedTypeName(x.Name, "Immutable"))
                .ToList());
        }

        if (model.CsType.ModelCsType == ModelCsType.Interface)
            model.SetCsTypeCore(model.CsType.MutateName(MetadataTypeConverter.RemoveInterfacePrefix(model.CsType.Name)));

        if (!typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(IsModelPropertyAttribute))
            .Select(prop => ParsePropertyCore(prop, model))
            .Transpose()
            .TryUnwrap(out var properties, out var propFailures))
            return DLOptionFailure.Fail($"Parsing properties", model, propFailures);

        model.AddPropertiesCore(properties);

        model.SetUsingsCore(ParseUsings(typeSyntax.SyntaxTree));

        return model;
    }

    private Option<MetadataModelDraft, IDLOptionFailure> ParseModelDraft(TypeDeclarationSyntax typeSyntax)
    {
        var csType = CsTypeDeclarationSyntax.Create(typeSyntax);
        var csFile = !string.IsNullOrEmpty(typeSyntax.SyntaxTree.FilePath)
            ? new CsFileDeclaration(typeSyntax.SyntaxTree.FilePath)
            : (CsFileDeclaration?)null;
        var sourceSpan = new SourceTextSpan(typeSyntax.SpanStart, typeSyntax.Span.Length);

        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes for {typeSyntax.Identifier.Text}", failures);

        var modelInstanceInterfaces = parsedAttributes
            .Where(x => x is InterfaceAttribute interfaceAttribute && interfaceAttribute.GenerateInterface)
            .Select(x => x as InterfaceAttribute)
            .Select(x => new CsTypeDeclaration(x?.Name ?? $"I{csType.Name}", csType.Namespace, ModelCsType.Interface))
            .ToList();

        if (modelInstanceInterfaces.Count > 1)
            return FailType(
                typeSyntax,
                DLFailureType.InvalidArgument,
                $"Multiple model instance interfaces ({modelInstanceInterfaces.Select(x => x.Name).ToJoinedString(", ")}) found in model");

        CsTypeDeclaration[] interfaces = [];
        if (typeSyntax.BaseList != null)
        {
            if (!ParseDeclaredInterfaces(typeSyntax).TryUnwrap(out var declaredInterfaces, out var interfaceFailure))
                return DLOptionFailure.Fail($"Parsing base interfaces for {typeSyntax.Identifier.Text}", [interfaceFailure]);

            interfaces = declaredInterfaces
                .Where(x => !MatchesUnqualifiedTypeName(x.Name, "Immutable"))
                .ToArray();
        }

        if (csType.ModelCsType == ModelCsType.Interface)
            csType = csType.MutateName(MetadataTypeConverter.RemoveInterfacePrefix(csType.Name));

        if (!typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(IsModelPropertyAttribute))
            .Select(ParsePropertyDraft)
            .Transpose()
            .TryUnwrap(out var properties, out var propFailures))
            return DLOptionFailure.Fail($"Parsing properties for {typeSyntax.Identifier.Text}", propFailures);

        var valueProperties = properties
            .OfType<MetadataValuePropertyDraft>()
            .ToArray();
        var relationProperties = properties
            .OfType<MetadataRelationPropertyDraft>()
            .ToArray();
        var usings = ParseUsings(typeSyntax.SyntaxTree);

        return new MetadataModelDraft(csType)
        {
            CsFile = csFile,
            SourceSpan = sourceSpan,
            Attributes = parsedAttributes,
            AttributeSourceSpans = attributeSourceSpans,
            ModelInstanceInterface = modelInstanceInterfaces.Count == 1
                ? modelInstanceInterfaces[0]
                : null,
            OriginalInterfaces = interfaces,
            Usings = usings,
            ValueProperties = valueProperties,
            RelationProperties = relationProperties
        };
    }

    internal static ModelUsing[] ParseUsings(SyntaxTree syntaxTree)
    {
        return syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(uds => uds?.Name?.ToString())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .OrderBy(ns => ns!.StartsWith("System"))
            .ThenBy(ns => ns)
            .Select(ns => new ModelUsing(ns!))
            .ToArray();
    }

    private static Option<MetadataTableDraft, IDLOptionFailure> ParseTableDraft(MetadataModelDraft model)
    {
        TableType tableType;
        foreach (var originalInterface in model.OriginalInterfaces)
        {
            if (TryGetInvalidModelInterfaceContractArity(
                originalInterface.Name,
                out var contractName,
                out var typeArgumentCount,
                out var expectedDescription))
                return FailModelDraft(
                    model,
                    DLFailureType.InvalidModel,
                    $"Model '{model.CsType.Name}' declared DataLinq model contract '{originalInterface.Name}' with {typeArgumentCount} type arguments. '{contractName}' {expectedDescription}.");
        }

        if (model.OriginalInterfaces.Any(x => IsTableModelContract(x.Name)))
            tableType = TableType.Table;
        else if (model.OriginalInterfaces.Any(x => IsViewModelContract(x.Name)))
            tableType = TableType.View;
        else
            return FailModelDraft(model, DLFailureType.InvalidModel, $"Model '{model.CsType.Name}' does not inherit from 'ITableModel' or 'IViewModel'.");

        var dbName = model.CsType.Name;
        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                dbName = tableAttribute.Name;

            if (attribute is ViewAttribute viewAttribute)
                dbName = viewAttribute.Name;
        }

        return new MetadataTableDraft(dbName)
        {
            Type = tableType,
            Definition = model.Attributes
                .OfType<DefinitionAttribute>()
                .LastOrDefault()?
                .Sql,
            UseCache = model.Attributes
                .OfType<UseCacheAttribute>()
                .LastOrDefault()?
                .UseCache,
            CacheLimits = model.Attributes
                .OfType<CacheLimitAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            IndexCache = model.Attributes
                .OfType<IndexCacheAttribute>()
                .Select(x => (x.Type, x.Amount))
                .ToArray()
        };
    }

    private static Option<CsTypeDeclaration, IDLOptionFailure> ParseDeclaredInterface(BaseTypeSyntax baseType)
    {
        try
        {
            var declaration = CsTypeDeclarationSyntax.Create(baseType);
            return new CsTypeDeclaration(GetUnqualifiedTypeName(baseType.Type), declaration.Namespace, ModelCsType.Interface);
        }
        catch (NotImplementedException exception)
        {
            return FailType(
                baseType.Type,
                DLFailureType.InvalidModel,
                $"Base type '{baseType.Type}' uses unsupported C# type syntax: {exception.Message}");
        }
    }

    private Option<IReadOnlyList<CsTypeDeclaration>, IDLOptionFailure> ParseDeclaredInterfaces(TypeDeclarationSyntax typeSyntax)
    {
        var declarations = new List<CsTypeDeclaration>();
        if (typeSyntax.BaseList == null)
            return declarations;

        var visitedInterfaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var baseType in typeSyntax.BaseList.Types)
        {
            if (!ParseDeclaredInterfaceTree(
                baseType,
                declarations,
                visitedInterfaces,
                new Dictionary<string, string>(StringComparer.Ordinal)).TryUnwrap(out _, out var failure))
                return failure;
        }

        return declarations;
    }

    private Option<bool, IDLOptionFailure> ParseDeclaredInterfaceTree(
        BaseTypeSyntax baseType,
        List<CsTypeDeclaration> declarations,
        HashSet<string> visitedInterfaces,
        IReadOnlyDictionary<string, string> typeParameterSubstitutions)
    {
        if (!ParseDeclaredInterface(baseType).TryUnwrap(out var declaration, out var failure))
            return failure;

        declaration = declaration.MutateName(ApplyTypeParameterSubstitutions(declaration.Name, typeParameterSubstitutions));
        AddDeclaredInterface(declarations, declaration);

        var interfaceDeclaration = FindDeclaredInterfaceSyntax(declaration.Name);
        if (interfaceDeclaration?.BaseList == null)
            return true;

        if (!visitedInterfaces.Add(declaration.Name))
            return true;

        var inheritedSubstitutions = CreateTypeParameterSubstitutions(
            interfaceDeclaration,
            declaration.Name,
            typeParameterSubstitutions);

        foreach (var inheritedInterface in interfaceDeclaration.BaseList.Types)
        {
            if (!ParseDeclaredInterfaceTree(
                inheritedInterface,
                declarations,
                visitedInterfaces,
                inheritedSubstitutions).TryUnwrap(out _, out var inheritedFailure))
                return inheritedFailure;
        }

        return true;
    }

    private InterfaceDeclarationSyntax? FindDeclaredInterfaceSyntax(string interfaceName)
    {
        var lookupName = GetUnqualifiedGenericTypeDefinitionName(interfaceName);
        return modelSyntaxes
            .OfType<InterfaceDeclarationSyntax>()
            .FirstOrDefault(interfaceSyntax => interfaceSyntax.Identifier.Text == lookupName);
    }

    internal static string GetUnqualifiedGenericTypeDefinitionName(string typeName)
    {
        var unqualifiedTypeName = GetUnqualifiedTypeName(typeName);
        var genericStart = unqualifiedTypeName.IndexOf('<');
        return genericStart >= 0
            ? unqualifiedTypeName.Substring(0, genericStart).Trim()
            : unqualifiedTypeName;
    }

    private static void AddDeclaredInterface(List<CsTypeDeclaration> declarations, CsTypeDeclaration declaration)
    {
        if (declarations.Any(existing =>
            string.Equals(existing.Name, declaration.Name, StringComparison.Ordinal) &&
            string.Equals(existing.Namespace, declaration.Namespace, StringComparison.Ordinal)))
            return;

        declarations.Add(declaration);
    }

    public Option<Attribute, IDLOptionFailure> ParseAttribute(AttributeSyntax attributeSyntax)
    {
        var name = GetUnqualifiedAttributeName(attributeSyntax.Name);
        var generictype = GetGenericAttributeName(attributeSyntax.Name);
        var arguments = attributeSyntax.ArgumentList?.Arguments
            .Select(ParseAttributeArgument)
            .ToList() ?? [];

        if (name == "Database")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new DatabaseAttribute(arguments[0]);
        }

        if (name == "Table")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new TableAttribute(arguments[0]);
        }

        if (name == "View")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new ViewAttribute(arguments[0]);
        }

        if (name == "Column")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new ColumnAttribute(arguments[0]);
        }

        if (name == "ScalarConverter")
        {
            if (arguments.Count != 1 ||
                attributeSyntax.ArgumentList?.Arguments[0].Expression is not TypeOfExpressionSyntax typeOfExpression)
            {
                return FailAttribute(
                    attributeSyntax,
                    DLFailureType.InvalidArgument,
                    "Attribute 'ScalarConverter' requires exactly one typeof(converter) argument.");
            }

            return new ScalarConverterSourceAttribute(typeOfExpression.Type.ToString());
        }

        if (name == "Comment")
        {
            if (arguments.Count == 1)
                return new CommentAttribute(arguments[0]);

            if (arguments.Count == 2)
            {
                if (!Enum.TryParse(arguments[0].Split('.').Last(), out DatabaseType databaseType))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid DatabaseType value '{arguments[0]}'");

                return new CommentAttribute(databaseType, arguments[1]);
            }

            return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 1 or 2 arguments");
        }

        if (name == "Check")
        {
            if (arguments.Count == 2)
                return new CheckAttribute(arguments[0], arguments[1]);

            if (arguments.Count == 3)
            {
                if (!Enum.TryParse(arguments[0].Split('.').Last(), out DatabaseType databaseType))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid DatabaseType value '{arguments[0]}'");

                return new CheckAttribute(databaseType, arguments[1], arguments[2]);
            }

            return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "Definition")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have any arguments");

            return new DefinitionAttribute(arguments[0]);
        }

        if (name == "UseCache")
        {
            if (arguments.Count == 1)
            {
                if (!TryParseBool(arguments[0], out var useCache))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid boolean value '{arguments[0]}' for attribute '{name}'");

                return new UseCacheAttribute(useCache);
            }
            else
                return new UseCacheAttribute();
        }

        if (name == "CacheLimit")
        {
            if (arguments.Count != 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheLimitType limitType))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid CacheLimitType value '{arguments[0]}'");

            if (!TryParseLong(arguments[1], out var amount))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid cache limit amount '{arguments[1]}'");

            return new CacheLimitAttribute(limitType, amount);
        }

        if (name == "CacheCleanup")
        {
            if (arguments.Count != 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheCleanupType cleanupType))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid CacheCleanupType value '{arguments[0]}'");

            if (!TryParseLong(arguments[1], out var amount))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid cache cleanup amount '{arguments[1]}'");

            return new CacheCleanupAttribute(cleanupType, amount);
        }

        if (name == "IndexCache")
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 1 or 2 arguments");
            }

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out IndexCacheType indexCacheType))
            {
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid IndexCacheType value '{arguments[0]}'");
            }

            if (arguments.Count == 1)
                return new IndexCacheAttribute(indexCacheType);

            if (!TryParseInt(arguments[1], out var amount))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid index cache amount '{arguments[1]}'");

            return new IndexCacheAttribute(indexCacheType, amount);
        }

        if (name == "AutoIncrement")
        {
            if (arguments.Any())
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new AutoIncrementAttribute();
        }

        if (name == "Relation")
        {
            var rawArguments = attributeSyntax.ArgumentList?.Arguments.ToList() ?? [];
            if (arguments.Count == 2)
                return new RelationAttribute(arguments[0], ParseAttributeStringArrayArgument(rawArguments[1]));
            else if (arguments.Count == 3)
                return new RelationAttribute(arguments[0], ParseAttributeStringArrayArgument(rawArguments[1]), arguments[2]);
            else
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "PrimaryKey")
        {
            if (arguments.Any())
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new PrimaryKeyAttribute();
        }

        if (name == "ForeignKey")
        {
            if (arguments.Count == 3)
                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2]);

            if (arguments.Count == 4)
            {
                if (!TryParseInt(arguments[3], out var ordinal))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ordinal value '{arguments[3]}' for attribute '{name}'");

                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2], ordinal);
            }

            if (arguments.Count == 5)
            {
                if (!TryParseReferentialAction(arguments[3], out var onUpdate))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON UPDATE referential action '{arguments[3]}' for attribute '{name}'");

                if (!TryParseReferentialAction(arguments[4], out var onDelete))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON DELETE referential action '{arguments[4]}' for attribute '{name}'");

                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2], onUpdate, onDelete);
            }

            if (arguments.Count == 6)
            {
                if (!TryParseInt(arguments[3], out var compositeOrdinal))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ordinal value '{arguments[3]}' for attribute '{name}'");

                if (!TryParseReferentialAction(arguments[4], out var onUpdate))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON UPDATE referential action '{arguments[4]}' for attribute '{name}'");

                if (!TryParseReferentialAction(arguments[5], out var onDelete))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid ON DELETE referential action '{arguments[5]}' for attribute '{name}'");

                return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2], compositeOrdinal, onUpdate, onDelete);
            }

            return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' must have 3, 4, 5, or 6 arguments");
        }

        if (name == "Enum")
        {
            return new EnumAttribute([.. arguments]);
        }

        if (name == "Nullable")
        {
            if (arguments.Any())
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new NullableAttribute();
        }

        if (name == "Default")
        {
            if (arguments.Count != 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            var argument = attributeSyntax.ArgumentList!.Arguments[0];
            var codeExpression = argument.Expression.ToString();
            return new DefaultAttribute(ParseDefaultAttributeValue(argument), codeExpression);
        }

        if (name == "DefaultSql")
        {
            if (arguments.Count != 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' must have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out DatabaseType databaseType))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidType, $"Invalid DatabaseType value '{arguments[0]}'");

            return new DefaultSqlAttribute(databaseType, arguments[1]);
        }

        if (name == "DefaultCurrentTimestamp")
        {
            if (arguments.Count != 0)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            return new DefaultCurrentTimestampAttribute();
        }

        if (name == "DefaultNewUUID")
        {
            if (arguments.Count > 1)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

            if (arguments.Count == 1)
            {
                if (!UUIDVersion.TryParse(arguments[0], out UUIDVersion version))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid UUIDVersion value '{arguments[0]}'");

                return new DefaultNewUUIDAttribute(version);
            }
        
            return new DefaultNewUUIDAttribute();
        }

        if (name == "Index")
        {
            if (arguments.Count < 2)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            string indexName = arguments[0];
            if (string.IsNullOrWhiteSpace(indexName))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, "Index name cannot be empty.");

            if (!Enum.TryParse(arguments[1].Split('.').Last(), out IndexCharacteristic characteristic))
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid IndexCharacteristic value '{arguments[1]}'");

            if (arguments.Count == 2)
                return new IndexAttribute(arguments[0], characteristic);

            if (Enum.TryParse(arguments[2].Split('.').Last(), out IndexType type))
                return new IndexAttribute(indexName, characteristic, type, arguments.Skip(3).ToArray());
            else
                return new IndexAttribute(indexName, characteristic, arguments.Skip(2).ToArray());
        }


        if (name == "Type")
        {
            Option<Attribute, IDLOptionFailure> FailInvalidTypeArgument(string argumentName, string value) =>
                FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid TypeAttribute {argumentName} value '{value}'");

            if (arguments.Count == 0)
                return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");

            string enumValue = arguments[0].Split('.').Last();
            if (Enum.TryParse(enumValue, out DatabaseType dbType))
            {
                switch (arguments.Count)
                {
                    case 1: return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too few arguments");
                    case 2: return new TypeAttribute(dbType, arguments[1]);
                    case 3:
                        if (TryParseUlong(arguments[2], out ulong length))
                            return new TypeAttribute(dbType, arguments[1], length);

                        if (TryParseBool(arguments[2], out bool signed))
                            return new TypeAttribute(dbType, arguments[1], signed);

                        return FailInvalidTypeArgument("length or signed", arguments[2]);
                    case 4:
                        if (!TryParseUlong(arguments[2], out length))
                            return FailInvalidTypeArgument("length", arguments[2]);

                        if (TryParseUint(arguments[3], out uint decimals))
                            return new TypeAttribute(dbType, arguments[1], length, decimals);

                        if (TryParseBool(arguments[3], out signed))
                            return new TypeAttribute(dbType, arguments[1], length, signed);

                        return FailInvalidTypeArgument("decimals or signed", arguments[3]);
                    case 5:
                        if (!TryParseUlong(arguments[2], out length))
                            return FailInvalidTypeArgument("length", arguments[2]);

                        if (!TryParseUint(arguments[3], out decimals))
                            return FailInvalidTypeArgument("decimals", arguments[3]);

                        if (!TryParseBool(arguments[4], out signed))
                            return FailInvalidTypeArgument("signed", arguments[4]);

                        return new TypeAttribute(dbType, arguments[1], length, decimals, signed);
                }
            }
            else
            {
                switch (arguments.Count)
                {
                    case 1: return new TypeAttribute(arguments[0]);
                    case 2:
                        if (TryParseUlong(arguments[1], out ulong length))
                            return new TypeAttribute(arguments[0], length);

                        if (TryParseBool(arguments[1], out bool signed))
                            return new TypeAttribute(arguments[0], signed);

                        return FailInvalidTypeArgument("length or signed", arguments[1]);
                    case 3:
                        if (!TryParseUlong(arguments[1], out length))
                            return FailInvalidTypeArgument("length", arguments[1]);

                        if (!TryParseBool(arguments[2], out signed))
                            return FailInvalidTypeArgument("signed", arguments[2]);

                        return new TypeAttribute(arguments[0], length, signed);
                    case 4:
                        if (!TryParseUlong(arguments[1], out length))
                            return FailInvalidTypeArgument("length", arguments[1]);

                        if (!TryParseUint(arguments[2], out uint decimals))
                            return FailInvalidTypeArgument("decimals", arguments[2]);

                        if (!TryParseBool(arguments[3], out signed))
                            return FailInvalidTypeArgument("signed", arguments[3]);

                        return new TypeAttribute(arguments[0], length, decimals, signed);
                }
            }

            return FailAttribute(attributeSyntax, DLFailureType.NotImplemented, $"Attribute 'TypeAttribute' with {arguments.Count} arguments not implemented");
        }

        if (name == "Interface")
        {
            // Handle generic InterfaceAttribute<T>
            if (generictype != null)
            {
                if (generictype.TypeArgumentList.Arguments.Count != 1)
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' must have exactly one type argument");

                if (arguments.Count > 1)
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Attribute '{name}' have too many arguments");

                var typeArgument = GetUnqualifiedTypeName(generictype.TypeArgumentList.Arguments[0]);
                if (arguments.Count == 0)
                    return new InterfaceAttribute(typeArgument);

                if (!TryParseBool(arguments[0], out bool generateInterface))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid InterfaceAttribute generateInterface value '{arguments[0]}'");

                return new InterfaceAttribute(typeArgument, generateInterface);
            }

            if (arguments.Count == 1)
                return new InterfaceAttribute(arguments[0]);
            else if (arguments.Count == 2)
            {
                if (!TryParseBool(arguments[1], out bool generateInterface))
                    return FailAttribute(attributeSyntax, DLFailureType.InvalidArgument, $"Invalid InterfaceAttribute generateInterface value '{arguments[1]}'");

                return new InterfaceAttribute(arguments[0], generateInterface);
            }
            else
                return new InterfaceAttribute();
        }

        return FailAttribute(attributeSyntax, DLFailureType.NotImplemented, $"Attribute '{name}' not implemented");
    }

    private static bool IsModelPropertyAttribute(AttributeSyntax attributeSyntax)
    {
        var name = GetUnqualifiedAttributeName(attributeSyntax.Name);
        return name == "Column" || name == "Relation";
    }

    internal static string GetUnqualifiedAttributeName(NameSyntax nameSyntax)
    {
        var name = nameSyntax switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualifiedName => GetUnqualifiedAttributeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => GetUnqualifiedAttributeName(aliasQualifiedName.Name),
            _ => nameSyntax.ToString()
        };

        const string attributeSuffix = "Attribute";
        return name.EndsWith(attributeSuffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - attributeSuffix.Length)
            : name;
    }

    private static GenericNameSyntax? GetGenericAttributeName(NameSyntax nameSyntax)
    {
        return nameSyntax switch
        {
            GenericNameSyntax genericName => genericName,
            QualifiedNameSyntax qualifiedName => GetGenericAttributeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => GetGenericAttributeName(aliasQualifiedName.Name),
            _ => null
        };
    }

    private static string ParseAttributeArgument(AttributeArgumentSyntax argument)
    {
        if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is string stringValue)
            return stringValue;

        return argument.Expression.ToString().Trim('"');
    }

    private static object ParseDefaultAttributeValue(AttributeArgumentSyntax argument)
    {
        return TryParseLiteralAttributeValue(argument.Expression, out var value)
            ? value
            : ParseAttributeArgument(argument);
    }

    private static bool TryParseLiteralAttributeValue(ExpressionSyntax expression, out object value)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            return TryParseLiteralAttributeValue(parenthesizedExpression.Expression, out value);

        if (expression is LiteralExpressionSyntax literal && literal.Token.Value is { } literalValue)
        {
            value = literalValue;
            return true;
        }

        if (expression is CastExpressionSyntax castExpression &&
            TryParseLiteralAttributeValue(castExpression.Expression, out var castOperand) &&
            TryCastLiteralValue(castExpression.Type, castOperand, out value))
        {
            return true;
        }

        if (expression is PrefixUnaryExpressionSyntax unaryExpression)
        {
            if (unaryExpression.OperatorToken.Text == "+")
                return TryParseLiteralAttributeValue(unaryExpression.Operand, out value);

            if (unaryExpression.OperatorToken.Text == "-" &&
                TryParseLiteralAttributeValue(unaryExpression.Operand, out var operand) &&
                TryNegateLiteralValue(operand, out value))
            {
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static bool TryCastLiteralValue(TypeSyntax typeSyntax, object operand, out object value)
    {
        var typeName = GetUnqualifiedTypeName(typeSyntax);

        try
        {
            value = typeName switch
            {
                "sbyte" => Convert.ToSByte(operand, CultureInfo.InvariantCulture),
                "byte" => Convert.ToByte(operand, CultureInfo.InvariantCulture),
                "short" => Convert.ToInt16(operand, CultureInfo.InvariantCulture),
                "ushort" => Convert.ToUInt16(operand, CultureInfo.InvariantCulture),
                "int" => Convert.ToInt32(operand, CultureInfo.InvariantCulture),
                "uint" => Convert.ToUInt32(operand, CultureInfo.InvariantCulture),
                "long" => Convert.ToInt64(operand, CultureInfo.InvariantCulture),
                "ulong" => Convert.ToUInt64(operand, CultureInfo.InvariantCulture),
                "float" => Convert.ToSingle(operand, CultureInfo.InvariantCulture),
                "double" => Convert.ToDouble(operand, CultureInfo.InvariantCulture),
                "decimal" => Convert.ToDecimal(operand, CultureInfo.InvariantCulture),
                "char" => Convert.ToChar(operand, CultureInfo.InvariantCulture),
                _ => null!
            };

            return value is not null;
        }
        catch
        {
            value = null!;
            return false;
        }
    }

    private static bool TryNegateLiteralValue(object operand, out object value)
    {
        switch (operand)
        {
            case sbyte typedValue:
                value = (sbyte)-typedValue;
                return true;
            case short typedValue:
                value = (short)-typedValue;
                return true;
            case int typedValue:
                value = -typedValue;
                return true;
            case long typedValue:
                value = -typedValue;
                return true;
            case float typedValue:
                value = -typedValue;
                return true;
            case double typedValue:
                value = -typedValue;
                return true;
            case decimal typedValue:
                value = -typedValue;
                return true;
            default:
                value = null!;
                return false;
        }
    }

    private static string[] ParseAttributeStringArrayArgument(AttributeArgumentSyntax argument)
    {
        if (argument.Expression is not ArrayCreationExpressionSyntax and not ImplicitArrayCreationExpressionSyntax)
            return [ParseAttributeArgument(argument)];

        var initializer = argument.Expression switch
        {
            ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer,
            ImplicitArrayCreationExpressionSyntax implicitArrayCreation => implicitArrayCreation.Initializer,
            _ => null
        };

        if (initializer == null)
            return [];

        return initializer.Expressions
            .Select(expression => expression is LiteralExpressionSyntax literal && literal.Token.Value is string stringValue
                ? stringValue
                : expression.ToString().Trim('"'))
            .ToArray();
    }

    private static Option<Attribute, IDLOptionFailure> FailAttribute(AttributeSyntax attributeSyntax, DLFailureType type, string message)
        => TryGetSourceLocation(attributeSyntax, out var sourceLocation)
            ? DLOptionFailure.Fail(type, message, sourceLocation)
            : DLOptionFailure.Fail(type, message);

    private static bool TryParseBool(string value, out bool result) =>
        bool.TryParse(value, out result);

    private static bool TryParseLong(string value, out long result) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseReferentialAction(string value, out ReferentialAction result) =>
        Enum.TryParse(value.Split('.').Last(), out result);

    private static bool TryParseUlong(string value, out ulong result) =>
        ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseUint(string value, out uint result) =>
        uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryGetSourceLocation(SyntaxNode syntaxNode, out SourceLocation sourceLocation)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            sourceLocation = default;
            return false;
        }

        var file = new CsFileDeclaration(filePath);
        sourceLocation = new SourceLocation(file, new SourceTextSpan(syntaxNode.SpanStart, syntaxNode.Span.Length));
        return true;
    }

    [Obsolete(MetadataMutationGuard.MutableFactoryHelperObsoleteMessage)]
    public Option<PropertyDefinition, IDLOptionFailure> ParseProperty(PropertyDeclarationSyntax propSyntax, ModelDefinition model)
    {
        return ParsePropertyCore(propSyntax, model);
    }

    internal Option<PropertyDefinition, IDLOptionFailure> ParsePropertyCore(PropertyDeclarationSyntax propSyntax, ModelDefinition model)
    {
        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in propSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes for {propSyntax.Identifier.Text}", model, failures);

        var isRelationProperty = parsedAttributes.Any(attribute => attribute is RelationAttribute);
        if (!ParsePropertyCsType(propSyntax, preserveSourceTypeName: !isRelationProperty).TryUnwrap(out var csType, out var csTypeFailure))
            return csTypeFailure;

        var enumDeclaration = FindEnumDeclaration(csType.Name, propSyntax);
        var enumDeclarationInPropertyFile = IsDeclaredInSameSyntaxTree(propSyntax, enumDeclaration);
        if (enumDeclaration != null)
            csType = CreateEnumCsTypeDeclaration(csType, enumDeclaration);

        PropertyDefinition property = isRelationProperty
            ? new RelationProperty(propSyntax.Identifier.Text, csType, model, parsedAttributes)
            : new ValueProperty(propSyntax.Identifier.Text, csType, model, parsedAttributes);

        property.SetCsNullableCore(propSyntax.Type is NullableTypeSyntax);
        property.SetSourceInfoCore(new PropertySourceInfo(
            new SourceTextSpan(propSyntax.SpanStart, propSyntax.Span.Length),
            GetDefaultValueExpressionSourceSpan(propSyntax)));
        foreach (var (attribute, sourceSpan) in attributeSourceSpans)
            property.SetAttributeSourceSpanCore(attribute, sourceSpan);

        if (property is ValueProperty valueProp)
        {
            if (!TryGetSingleEnumAttribute(propSyntax, parsedAttributes, out var enumAttribute, out var enumFailure))
                return enumFailure!;

            var parentClassSyntax = propSyntax.Parent as TypeDeclarationSyntax;

            if (enumAttribute != null || enumDeclaration != null)
            {
                valueProp.SetCsSizeCore(MetadataTypeConverter.CsTypeSize("enum"));

                var enumValueList = enumAttribute?.Values.Select((x, i) => (x, i + 1)) ?? [];

                var declaredInClass = parentClassSyntax?.Members
                    .OfType<EnumDeclarationSyntax>()
                    .Any(enumDecl => enumDecl.Identifier.ValueText == valueProp.CsType.Name) ?? false;
                var declaredInModelFile = enumDeclarationInPropertyFile &&
                    (declaredInClass || !HasExternalTopLevelEnumDeclaration(
                        valueProp.CsType.Name,
                        enumDeclaration != null ? CsTypeDeclarationSyntax.GetNamespace(enumDeclaration) : CsTypeDeclarationSyntax.GetNamespace(propSyntax),
                        propSyntax));

                var csEnumValues = new List<(string name, int value)>();

                if (enumDeclaration != null)
                {
                    int lastValue = -1;
                    foreach (var member in enumDeclaration.Members)
                    {
                        if (member.EqualsValue != null)
                        {
                            var explicitValue = member.EqualsValue.Value.ToString();
                            if (!TryParseInt(explicitValue, out lastValue))
                                return FailProperty(member.EqualsValue.Value, DLFailureType.InvalidArgument, $"Invalid enum value '{explicitValue}' for enum member '{member.Identifier.ValueText}'. DataLinq enum metadata currently supports explicit integer values only.");
                        }
                        else
                        {
                            lastValue++;
                        }

                        csEnumValues.Add((member.Identifier.ValueText, lastValue));
                    }
                }

                valueProp.SetEnumPropertyCore(new EnumProperty(
                    enumValueList,
                    csEnumValues,
                    declaredInClass,
                    declaredInModelFile));
            }
            else
            {
                valueProp.SetCsSizeCore(MetadataTypeConverter.CsTypeSize(property.CsType.Name));
            }
        }

        return property;
    }

    private Option<object, IDLOptionFailure> ParsePropertyDraft(PropertyDeclarationSyntax propSyntax)
    {
        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var failures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in propSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Any())
            return DLOptionFailure.Fail($"Parsing attributes for {propSyntax.Identifier.Text}", failures);

        var isRelationProperty = parsedAttributes.Any(attribute => attribute is RelationAttribute);
        if (!ParsePropertyCsType(propSyntax, preserveSourceTypeName: !isRelationProperty).TryUnwrap(out var csType, out var csTypeFailure))
            return csTypeFailure;

        var enumDeclaration = FindEnumDeclaration(csType.Name, propSyntax);
        var enumDeclarationInPropertyFile = IsDeclaredInSameSyntaxTree(propSyntax, enumDeclaration);
        if (enumDeclaration != null)
            csType = CreateEnumCsTypeDeclaration(csType, enumDeclaration);

        var sourceInfo = new PropertySourceInfo(
            new SourceTextSpan(propSyntax.SpanStart, propSyntax.Span.Length),
            GetDefaultValueExpressionSourceSpan(propSyntax));

        if (isRelationProperty)
        {
            return new MetadataRelationPropertyDraft(propSyntax.Identifier.Text, csType)
            {
                Attributes = parsedAttributes,
                AttributeSourceSpans = attributeSourceSpans,
                SourceInfo = sourceInfo,
                CsNullable = propSyntax.Type is NullableTypeSyntax,
                RelationName = parsedAttributes
                    .OfType<RelationAttribute>()
                    .FirstOrDefault()?
                    .Name
            };
        }

        int? csSize;
        EnumProperty? enumProperty = null;
        if (!TryGetSingleEnumAttribute(propSyntax, parsedAttributes, out var enumAttribute, out var enumFailure))
            return enumFailure!;

        var parentClassSyntax = propSyntax.Parent as TypeDeclarationSyntax;

        if (enumAttribute != null || enumDeclaration != null)
        {
            csSize = MetadataTypeConverter.CsTypeSize("enum");

            var enumValueList = enumAttribute?.Values.Select((x, i) => (x, i + 1)) ?? [];
            var declaredInClass = parentClassSyntax?.Members
                .OfType<EnumDeclarationSyntax>()
                .Any(enumDecl => enumDecl.Identifier.ValueText == csType.Name) ?? false;
            var declaredInModelFile = enumDeclarationInPropertyFile &&
                (declaredInClass || !HasExternalTopLevelEnumDeclaration(
                    csType.Name,
                    enumDeclaration != null ? CsTypeDeclarationSyntax.GetNamespace(enumDeclaration) : CsTypeDeclarationSyntax.GetNamespace(propSyntax),
                    propSyntax));
            var csEnumValues = new List<(string name, int value)>();

            if (enumDeclaration != null)
            {
                int lastValue = -1;
                foreach (var member in enumDeclaration.Members)
                {
                    if (member.EqualsValue != null)
                    {
                        var explicitValue = member.EqualsValue.Value.ToString();
                        if (!TryParseInt(explicitValue, out lastValue))
                            return FailProperty(member.EqualsValue.Value, DLFailureType.InvalidArgument, $"Invalid enum value '{explicitValue}' for enum member '{member.Identifier.ValueText}'. DataLinq enum metadata currently supports explicit integer values only.");
                    }
                    else
                    {
                        lastValue++;
                    }

                    csEnumValues.Add((member.Identifier.ValueText, lastValue));
                }
            }

            enumProperty = new EnumProperty(
                enumValueList,
                csEnumValues,
                declaredInClass,
                declaredInModelFile);
        }
        else
        {
            csSize = MetadataTypeConverter.CsTypeSize(csType.Name);
        }

        return new MetadataValuePropertyDraft(
            propSyntax.Identifier.Text,
            csType,
            ParseColumnDraft(propSyntax.Identifier.Text, parsedAttributes))
        {
            Attributes = parsedAttributes,
            AttributeSourceSpans = attributeSourceSpans,
            SourceInfo = sourceInfo,
            CsNullable = propSyntax.Type is NullableTypeSyntax,
            CsSize = csSize,
            EnumProperty = enumProperty
        };
    }

    private static bool TryGetSingleEnumAttribute(
        PropertyDeclarationSyntax propSyntax,
        IEnumerable<Attribute> attributes,
        out EnumAttribute? enumAttribute,
        out IDLOptionFailure? failure)
    {
        var enumAttributes = attributes.OfType<EnumAttribute>().ToArray();
        if (enumAttributes.Length > 1)
        {
            enumAttribute = null;
            failure = FailProperty(
                propSyntax,
                DLFailureType.InvalidModel,
                $"Property '{propSyntax.Identifier.Text}' defines multiple Enum attributes. A property can have only one Enum attribute.");
            return false;
        }

        enumAttribute = enumAttributes.SingleOrDefault();
        failure = null;
        return true;
    }

    private static Option<CsTypeDeclaration, IDLOptionFailure> ParsePropertyCsType(
        PropertyDeclarationSyntax propSyntax,
        bool preserveSourceTypeName)
    {
        try
        {
            return preserveSourceTypeName
                ? CsTypeDeclarationSyntax.CreatePreservingSourceType(propSyntax)
                : CsTypeDeclarationSyntax.Create(propSyntax);
        }
        catch (NotImplementedException exception)
        {
            return FailProperty(
                propSyntax.Type,
                DLFailureType.InvalidModel,
                $"Property '{propSyntax.Identifier.Text}' uses unsupported C# type syntax '{propSyntax.Type}': {exception.Message}");
        }
    }

    private static MetadataColumnDraft ParseColumnDraft(string propertyName, IEnumerable<Attribute> attributes)
    {
        var columnName = propertyName;
        foreach (var attribute in attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                columnName = columnAttribute.Name;
        }

        return new MetadataColumnDraft(columnName)
        {
            Nullable = attributes.Any(x => x is NullableAttribute),
            AutoIncrement = attributes.Any(x => x is AutoIncrementAttribute),
            PrimaryKey = attributes.Any(x => x is PrimaryKeyAttribute),
            ForeignKey = attributes.Any(x => x is ForeignKeyAttribute),
            DbTypes = attributes
                .OfType<TypeAttribute>()
                .Select(x => new DatabaseColumnType(x.DatabaseType, x.Name, x.Length, x.Decimals, x.Signed))
                .ToArray()
        };
    }

    private static bool IsDeclaredInSameSyntaxTree(SyntaxNode source, SyntaxNode? declaration)
    {
        if (declaration == null)
            return false;

        if (ReferenceEquals(source.SyntaxTree, declaration.SyntaxTree))
            return true;

        return !string.IsNullOrWhiteSpace(source.SyntaxTree.FilePath) &&
            string.Equals(source.SyntaxTree.FilePath, declaration.SyntaxTree.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasExternalTopLevelEnumDeclaration(string enumName, string enumNamespace, SyntaxNode source)
    {
        return enumSyntaxes
            .Where(e => e.Identifier.ValueText == enumName)
            .Where(e => !IsDeclaredInSameSyntaxTree(source, e))
            .Any(e => string.Equals(CsTypeDeclarationSyntax.GetNamespace(e), enumNamespace, StringComparison.Ordinal));
    }

    private EnumDeclarationSyntax? FindEnumDeclaration(string enumName, SyntaxNode source)
    {
        var allEnums = modelSyntaxes
            .SelectMany(x => x.DescendantNodesAndSelf().OfType<EnumDeclarationSyntax>())
            .Concat(enumSyntaxes);

        return allEnums.FirstOrDefault(e => e.Identifier.ValueText == enumName && IsDeclaredInSameSyntaxTree(source, e))
            ?? allEnums.FirstOrDefault(e => e.Identifier.ValueText == enumName);
    }

    private static CsTypeDeclaration CreateEnumCsTypeDeclaration(
        CsTypeDeclaration csType,
        EnumDeclarationSyntax enumDeclaration) =>
        new(csType.Name, CsTypeDeclarationSyntax.GetNamespace(enumDeclaration), ModelCsType.Enum);

    private static SourceTextSpan? GetDefaultValueExpressionSourceSpan(PropertyDeclarationSyntax propSyntax)
    {
        var defaultExpression = propSyntax.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr => GetUnqualifiedAttributeName(attr.Name) == "Default")
            .Select(attr => attr.ArgumentList?.Arguments.SingleOrDefault()?.Expression)
            .FirstOrDefault(expr => expr != null);

        if (defaultExpression == null)
            return null;

        return new SourceTextSpan(defaultExpression.SpanStart, defaultExpression.Span.Length);
    }

    public Option<(string csPropertyName, TypeDeclarationSyntax? classSyntax), IDLOptionFailure> GetTableType(
        PropertyDeclarationSyntax property,
        List<TypeDeclarationSyntax> modelTypeSyntaxes,
        bool allowMissingTableModel = true,
        IReadOnlyCollection<TypeDeclarationSyntax>? declaredTypeSyntaxes = null)
    {
        var propType = property.Type;

        if (TryGetDbReadGenericType(propType, out var genericType))
        {
            if (genericType.TypeArgumentList.Arguments.Count != 1)
                return FailProperty(property, DLFailureType.InvalidModel, $"Table property '{property.Identifier.Text}' must use DbRead with exactly one model type argument.");

            var typeArgument = genericType.TypeArgumentList.Arguments[0];
            if (TryGetSimpleModelTypeName(typeArgument, out var modelTypeName))
            {
                var modelClass = modelTypeSyntaxes.FirstOrDefault(cls => cls.Identifier.Text == modelTypeName);
                modelClass ??= declaredTypeSyntaxes?.FirstOrDefault(cls => cls.Identifier.Text == modelTypeName);

                if (modelClass == null && !allowMissingTableModel)
                    return FailProperty(property, DLFailureType.InvalidModel, $"Table property '{property.Identifier.Text}' references model '{modelTypeName}', but no matching table or view model declaration was found.");

                return (property.Identifier.Text, modelClass);
            }

            return FailProperty(property, DLFailureType.InvalidModel, $"Table property '{property.Identifier.Text}' must use a simple model type argument. Found '{genericType.TypeArgumentList.Arguments[0]}'.");
        }

        return FailProperty(property, DLFailureType.NotImplemented, $"Table type {propType} is not implemented.");
    }

    private static bool TryGetSimpleModelTypeName(TypeSyntax typeSyntax, out string modelTypeName)
    {
        switch (typeSyntax)
        {
            case IdentifierNameSyntax identifierName:
                modelTypeName = identifierName.Identifier.Text;
                return true;
            case QualifiedNameSyntax qualifiedName:
                return TryGetSimpleModelTypeName(qualifiedName.Right, out modelTypeName);
            case AliasQualifiedNameSyntax { Name: IdentifierNameSyntax identifierName }:
                modelTypeName = identifierName.Identifier.Text;
                return true;
            default:
                modelTypeName = GetUnqualifiedTypeName(typeSyntax);
                return false;
        }
    }

    internal static bool IsDbReadTableType(TypeSyntax typeSyntax) =>
        TryGetDbReadGenericType(typeSyntax, out _);

    private static bool TryGetDbReadGenericType(TypeSyntax typeSyntax, out GenericNameSyntax genericType)
    {
        if (typeSyntax is GenericNameSyntax directGeneric && directGeneric.Identifier.Text == "DbRead")
        {
            genericType = directGeneric;
            return true;
        }

        if (typeSyntax is QualifiedNameSyntax { Right: GenericNameSyntax qualifiedGeneric } &&
            qualifiedGeneric.Identifier.Text == "DbRead")
        {
            genericType = qualifiedGeneric;
            return true;
        }

        genericType = null!;
        return false;
    }

    private static IDLOptionFailure FailProperty(SyntaxNode syntaxNode, DLFailureType type, string message)
        => TryGetSourceLocation(syntaxNode, out var sourceLocation)
            ? DLOptionFailure.Fail(type, message, sourceLocation)
            : DLOptionFailure.Fail(type, message);

    private static IDLOptionFailure FailType(SyntaxNode syntaxNode, DLFailureType type, string message) =>
        TryGetSourceLocation(syntaxNode, out var sourceLocation)
            ? DLOptionFailure.Fail(type, message, sourceLocation)
            : DLOptionFailure.Fail(type, message);

    private static IDLOptionFailure FailModelDraft(MetadataModelDraft model, DLFailureType type, string message)
    {
        if (model.CsFile.HasValue)
            return DLOptionFailure.Fail(type, message, new SourceLocation(model.CsFile.Value, model.SourceSpan));

        return DLOptionFailure.Fail(type, message);
    }

    private bool InheritsFrom(CsTypeDeclaration decl, string typeName)
    {
        var matchingSyntax = modelSyntaxes.FirstOrDefault(ts => ts.Identifier.Text == decl.Name);
        if (matchingSyntax == null)
            return false;

        // If the matching syntax has a BaseList, check each base type.
        if (matchingSyntax.BaseList != null)
        {
            foreach (var baseTypeSyntax in matchingSyntax.BaseList.Types)
            {
                // Create a CsTypeDeclaration for the base type.
                var baseDecl = CsTypeDeclarationSyntax.Create(baseTypeSyntax);

                // Direct match: if the baseDecl's name is the type of interest.
                if (MatchesUnqualifiedTypeName(baseDecl.Name, typeName))
                    return true;

                // Recursively check if the base type inherits from the type.
                if (InheritsFrom(baseDecl, typeName))
                    return true;
            }
        }
        return false;
    }
}
