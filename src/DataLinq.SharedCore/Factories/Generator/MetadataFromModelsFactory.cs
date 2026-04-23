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

    public List<Option<DatabaseDefinition, IDLOptionFailure>> ReadSyntaxTrees(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes) => DLOptionFailure.CatchAll(() =>
    {
        var syntaxParser = new SyntaxParser(modelSyntaxes);

        // Identify classes implementing the interfaces of interest
        var dbModelClasses = modelSyntaxes
            .OfType<ClassDeclarationSyntax>()
            .Where(cls => ImplementsInterface(cls, modelSyntaxes, x => x == "IDatabaseModel"))
            .ToList();

        var parsedDatabases = ParseDatabaseModels(modelSyntaxes, syntaxParser, dbModelClasses).ToList();
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

    private static bool ImplementsInterface(TypeDeclarationSyntax type, ImmutableArray<TypeDeclarationSyntax> modelSyntaxes, Func<string, bool> interfaceNameFunc)
    {
        if (type.BaseList == null) return false;
        foreach (var baseType in type.BaseList.Types)
        {
            var baseTypeName = baseType.Type.ToString();
            if (interfaceNameFunc(baseTypeName))
                return true;
            var interfaceDecl = modelSyntaxes
                .OfType<InterfaceDeclarationSyntax>()
                .FirstOrDefault(i => i.Identifier.Text == baseTypeName);
            if (interfaceDecl != null && ImplementsInterface(interfaceDecl, modelSyntaxes, interfaceNameFunc))
                return true;
        }
        return false;
    }

    private static IEnumerable<Option<DatabaseDefinition, IDLOptionFailure>> ParseDatabaseModels(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes, SyntaxParser syntaxParser, List<ClassDeclarationSyntax> dbModelClasses)
    {
        foreach (var dbType in dbModelClasses)
            yield return ParseDatabaseModel(modelSyntaxes, syntaxParser, dbType);
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseModel(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes, SyntaxParser syntaxParser, ClassDeclarationSyntax dbType)
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
        var dbAttribute = parsedAttributes.OfType<DatabaseAttribute>().FirstOrDefault();
        // Use the attribute name if present, otherwise fall back to the C# class name.
        var logicalName = dbAttribute?.Name ?? MetadataTypeConverter.RemoveInterfacePrefix(dbType.Identifier.Text);

        // Step 3: Create the DatabaseDefinition with the correct logical name.
        var database = new DatabaseDefinition(logicalName, new CsTypeDeclaration(dbType));
        database.SetSourceSpan(new SourceTextSpan(dbType.SpanStart, dbType.Span.Length));
        database.SetAttributes(parsedAttributes);
        foreach (var (attribute, sourceSpan) in attributeSourceSpans)
            database.SetAttributeSourceSpan(attribute, sourceSpan);

        database.ParseAttributes(); // This will set the DbName, which might be the same as the logical name.

        if (!string.IsNullOrEmpty(dbType.SyntaxTree.FilePath))
            database.SetCsFile(new CsFileDeclaration(dbType.SyntaxTree.FilePath));

        var modelClasses = modelSyntaxes
            .Where(x =>
                ImplementsInterface(x, modelSyntaxes, i => i.Contains($"<{dbType.Identifier.Text}>")) &&
                   (ImplementsInterface(x, modelSyntaxes, i => i.StartsWith("ITableModel")) ||
                    ImplementsInterface(x, modelSyntaxes, i => i.StartsWith("IViewModel"))))
            .ToList();

        if (!dbType.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.Type is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
            .Select(prop => syntaxParser.GetTableType(prop, modelClasses))
            .Transpose()
            .Map(x => x.Select(t => syntaxParser.ParseTableModel(database, t.classSyntax, t.csPropertyName)))
            .FlatMap(x => x.Transpose())
            .TryUnwrap(out var models, out var modelFailures))
            return DLOptionFailure.AggregateFail(modelFailures);

        database.SetTableModels(models);

        if (!MetadataFactory.ValidateUniqueTableNames(database).TryUnwrap(out _, out var duplicateFailure))
            return duplicateFailure;

        if (!MetadataFactory.ParseIndices(database).TryUnwrap(out _, out var indexFailure))
            return indexFailure;

        if (!MetadataFactory.ParseRelations(database).TryUnwrap(out _, out var relationFailure))
            return relationFailure;

        if (database.TableModels.Any(x => x.CsPropertyName == database.CsType.Name))
            database.SetCsType(database.CsType.MutateName($"{database.CsType.Name}Db"));

        return database;
    }
}
