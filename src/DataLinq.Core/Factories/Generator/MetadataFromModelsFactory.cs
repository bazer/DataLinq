using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
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

        return ParseDatabaseModels(modelSyntaxes, syntaxParser, dbModelClasses).ToList();
    });

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

        var name = MetadataTypeConverter.RemoveInterfacePrefix(dbType.Identifier.Text);
        var database = new DatabaseDefinition(name, new CsTypeDeclaration(dbType));

        if (!string.IsNullOrEmpty(dbType.SyntaxTree.FilePath))
            database.SetCsFile(new CsFileDeclaration(dbType.SyntaxTree.FilePath));

        var modelClasses = modelSyntaxes
            .Where(x => 
                ImplementsInterface(x, modelSyntaxes, x => x.Contains($"<{dbType.Identifier.Text}>")) && 
                   (ImplementsInterface(x, modelSyntaxes, x => x.StartsWith("ITableModel")) || 
                    ImplementsInterface(x, modelSyntaxes, x => x.StartsWith("IViewModel"))))
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

        if (!dbType.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(x => syntaxParser.ParseAttribute(x))
            .Transpose()
            .TryUnwrap(out var attributes, out var attrFailures))
            return DLOptionFailure.AggregateFail(attrFailures);

        database.SetAttributes(attributes);
        database.ParseAttributes();

        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        if (database.TableModels.Any(x => x.CsPropertyName == database.CsType.Name))
            database.SetCsType(database.CsType.MutateName($"{database.CsType.Name}Db"));

        return database;
    }
}