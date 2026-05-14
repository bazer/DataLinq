using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories.Models;

public enum MetadataFromInterfacesFactoryError
{
    CompilationError,
    TypeNotFound,
    FileNotFound,
    CouldNotLoadAssembly
}

public class MetadataFromInterfacesFactoryOptions
{
    public Encoding FileEncoding { get; set; } = new UTF8Encoding(false);
    public bool RemoveInterfacePrefix { get; set; } = true;
    public bool AllowMissingTableModels { get; set; }
}

public class MetadataFromModelsFactory
{
    private readonly MetadataFromInterfacesFactoryOptions options;
    public Action<string>? Log { get; }

    public MetadataFromModelsFactory(MetadataFromInterfacesFactoryOptions options, Action<string>? log = null)
    {
        this.options = options;
        Log = log;
    }

    public List<Option<DatabaseDefinition, IDLOptionFailure>> ReadSyntaxTrees(
        ImmutableArray<TypeDeclarationSyntax> modelSyntaxes,
        ImmutableArray<EnumDeclarationSyntax> enumSyntaxes = default) => DLOptionFailure.CatchAll(() =>
    {
        var syntaxParser = new SyntaxParser(modelSyntaxes, enumSyntaxes);

        // Identify classes implementing the interfaces of interest
        var dbModelClasses = modelSyntaxes
            .OfType<ClassDeclarationSyntax>()
            .Where(cls => ImplementsInterface(cls, modelSyntaxes, SyntaxParser.IsDatabaseModelContract))
            .ToList();

        var parsedDatabases = ParseDatabaseModels(modelSyntaxes, syntaxParser, dbModelClasses, options.AllowMissingTableModels).ToList();
        return ValidateUniqueDatabaseNames(parsedDatabases);
    });

    private static List<Option<DatabaseDefinition, IDLOptionFailure>> ValidateUniqueDatabaseNames(List<Option<DatabaseDefinition, IDLOptionFailure>> parsedDatabases)
    {
        var duplicateGroups = parsedDatabases
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .ToList();

        if (!duplicateGroups.Any())
            return parsedDatabases;

        var duplicateDatabases = duplicateGroups
            .SelectMany(group => group.Skip(1).Select(database => (Database: database, First: group.First())))
            .ToDictionary(x => x.Database, x => CreateDuplicateDatabaseFailure(x.First, x.Database));

        return parsedDatabases
            .Select(parsed =>
                parsed.HasValue && duplicateDatabases.TryGetValue(parsed.Value, out var failure)
                    ? Option.Fail<DatabaseDefinition, IDLOptionFailure>(failure)
                    : parsed)
            .ToList();
    }

    private static IDLOptionFailure CreateDuplicateDatabaseFailure(DatabaseDefinition first, DatabaseDefinition duplicate)
    {
        var message = $"Duplicate database definition for '{duplicate.Name}'. Models '{first.CsType.Name}' and '{duplicate.CsType.Name}' both map to the same generated database name.";
        var sourceLocation = GetDatabaseNameSourceLocation(duplicate);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, duplicate);
    }

    private static SourceLocation? GetDatabaseNameSourceLocation(DatabaseDefinition database)
    {
        var databaseAttribute = database.Attributes.FirstOrDefault(x => x is DatabaseAttribute);

        if (databaseAttribute != null)
        {
            var attributeLocation = database.GetAttributeSourceLocation(databaseAttribute);
            if (attributeLocation.HasValue)
                return attributeLocation;
        }

        return database.GetSourceLocation();
    }

    private static bool ImplementsInterface(TypeDeclarationSyntax type, ImmutableArray<TypeDeclarationSyntax> modelSyntaxes, Func<string, bool> interfaceNameFunc) =>
        ImplementsInterface(
            type,
            modelSyntaxes,
            interfaceNameFunc,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static bool ImplementsInterface(
        TypeDeclarationSyntax type,
        ImmutableArray<TypeDeclarationSyntax> modelSyntaxes,
        Func<string, bool> interfaceNameFunc,
        HashSet<string> visitedDeclarations,
        IReadOnlyDictionary<string, string> typeParameterSubstitutions)
    {
        if (!visitedDeclarations.Add(GetVisitedDeclarationKey(type, typeParameterSubstitutions)))
            return false;

        if (type.BaseList == null) return false;
        foreach (var baseType in type.BaseList.Types)
        {
            var baseTypeName = SyntaxParser.ApplyTypeParameterSubstitutions(
                SyntaxParser.GetUnqualifiedTypeName(baseType.Type),
                typeParameterSubstitutions);

            if (interfaceNameFunc(baseTypeName))
                return true;

            var interfaceDecl = modelSyntaxes
                .OfType<InterfaceDeclarationSyntax>()
                .FirstOrDefault(i => string.Equals(
                    i.Identifier.Text,
                    SyntaxParser.GetUnqualifiedGenericTypeDefinitionName(baseTypeName),
                    StringComparison.Ordinal));

            if (interfaceDecl == null)
                continue;

            var inheritedSubstitutions = SyntaxParser.CreateTypeParameterSubstitutions(
                interfaceDecl,
                baseTypeName,
                typeParameterSubstitutions);

            if (ImplementsInterface(interfaceDecl, modelSyntaxes, interfaceNameFunc, visitedDeclarations, inheritedSubstitutions))
                return true;
        }
        return false;
    }

    private static string GetVisitedDeclarationKey(
        TypeDeclarationSyntax type,
        IReadOnlyDictionary<string, string> typeParameterSubstitutions)
    {
        if (typeParameterSubstitutions.Count == 0)
            return type.Identifier.Text;

        return type.Identifier.Text + "<" + string.Join(
            ",",
            typeParameterSubstitutions
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Key + "=" + x.Value)) + ">";
    }

    private static IEnumerable<Option<DatabaseDefinition, IDLOptionFailure>> ParseDatabaseModels(
        ImmutableArray<TypeDeclarationSyntax> modelSyntaxes,
        SyntaxParser syntaxParser,
        List<ClassDeclarationSyntax> dbModelClasses,
        bool allowMissingTableModels)
    {
        foreach (var dbType in dbModelClasses)
            yield return ParseDatabaseModel(modelSyntaxes, syntaxParser, dbType, allowMissingTableModels);
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseModel(
        ImmutableArray<TypeDeclarationSyntax> modelSyntaxes,
        SyntaxParser syntaxParser,
        ClassDeclarationSyntax dbType,
        bool allowMissingTableModels)
    {
        if (dbType == null)
            return DLOptionFailure.Fail("Database model class not found");

        if (dbType.Identifier.Text == null)
            return DLOptionFailure.Fail("Database model class must have a name");

        // Step 1: Parse attributes from the database class syntax first.
        var attributeSourceSpans = new List<(Attribute Attribute, SourceTextSpan Span)>();
        var parsedAttributes = new List<Attribute>();
        var attrFailures = new List<IDLOptionFailure>();

        foreach (var attributeSyntax in dbType.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (syntaxParser.ParseAttribute(attributeSyntax).TryUnwrap(out var attribute, out var failure))
            {
                parsedAttributes.Add(attribute);
                attributeSourceSpans.Add((attribute, new SourceTextSpan(attributeSyntax.SpanStart, attributeSyntax.Span.Length)));
            }
            else
            {
                attrFailures.Add(failure);
            }
        }

        if (attrFailures.Any())
            return DLOptionFailure.AggregateFail(attrFailures);

        // Step 2: Find the [Database] attribute to determine the logical name.
        var logicalName = parsedAttributes
            .OfType<DatabaseAttribute>()
            .LastOrDefault()?
            .Name ?? MetadataTypeConverter.RemoveInterfacePrefix(dbType.Identifier.Text);
        var databaseCsType = CsTypeDeclarationSyntax.Create(dbType);

        var modelClasses = modelSyntaxes
            .Where(x => ImplementsInterface(
                x,
                modelSyntaxes,
                i => SyntaxParser.IsTableOrViewModelContractForDatabase(i, dbType.Identifier.Text)))
            .ToList();

        if (!dbType.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => SyntaxParser.IsDbReadTableType(prop.Type))
            .Select(prop => syntaxParser.GetTableType(prop, modelClasses, allowMissingTableModels || modelClasses.Count == 0, modelSyntaxes))
            .Transpose()
            .Map(x => x.Select(t => syntaxParser.ParseTableModelDraft(databaseCsType, t.classSyntax, t.csPropertyName)))
            .FlatMap(x => x.Transpose())
            .TryUnwrap(out var tableModels, out var modelFailures))
            return DLOptionFailure.AggregateFail(modelFailures);

        var database = new MetadataDatabaseDraft(logicalName, databaseCsType)
        {
            DbName = logicalName,
            CsFile = !string.IsNullOrEmpty(dbType.SyntaxTree.FilePath)
                ? new CsFileDeclaration(dbType.SyntaxTree.FilePath)
                : null,
            SourceSpan = new SourceTextSpan(dbType.SpanStart, dbType.Span.Length),
            Attributes = parsedAttributes,
            AttributeSourceSpans = attributeSourceSpans,
            UseCache = parsedAttributes
                .OfType<UseCacheAttribute>()
                .LastOrDefault()?
                .UseCache ?? false,
            CacheLimits = parsedAttributes
                .OfType<CacheLimitAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            CacheCleanup = parsedAttributes
                .OfType<CacheCleanupAttribute>()
                .Select(x => (x.LimitType, x.Amount))
                .ToArray(),
            IndexCache = parsedAttributes
                .OfType<IndexCacheAttribute>()
                .Select(x => (x.Type, x.Amount))
                .ToArray(),
            TableModels = tableModels
        };

        if (!new MetadataDefinitionFactory().Build(database).TryUnwrap(out var builtDatabase, out var buildFailure))
            return buildFailure;

        return builtDatabase;
    }
}
