using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Core.Factories.Models;

public enum MetadataFromFileFactoryError
{
    CompilationError,
    TypeNotFound,
    FileNotFound,
    CouldNotLoadAssembly
}

public class MetadataFromFileFactoryOptions
{
    public Encoding FileEncoding { get; set; } = new UTF8Encoding(false);
    public bool RemoveInterfacePrefix { get; set; } = true;
}

public class MetadataFromFileFactory
{
    private readonly MetadataFromFileFactoryOptions options;
    public Action<string>? Log { get; }

    public MetadataFromFileFactory(MetadataFromFileFactoryOptions options, Action<string>? log = null)
    {
        this.options = options;
        Log = log;
    }

    public DatabaseDefinition ReadFiles(string csType, IEnumerable<string> srcPaths)
    {
        var trees = new List<SyntaxTree>();

        foreach (var path in srcPaths)
        {
            var sourceFiles = Directory.Exists(path)
                ? new DirectoryInfo(path)
                    .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                    .Select(a => a.FullName)
                : new FileInfo(path).FullName.Yield();

            foreach (string file in sourceFiles)
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file, options.FileEncoding)));
        }

        var modelSyntaxes = trees
            .Where(x => x.HasCompilationUnitRoot)
            .SelectMany(x => x.GetCompilationUnitRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Where(cls => cls.BaseList?.Types.Any(baseType => SyntaxParser.IsModelInterface(baseType.ToString()) || SyntaxParser.IsCustomModelInterface(baseType.ToString())) == true))
            .ToImmutableArray();

        return ReadSyntaxTrees(modelSyntaxes);
    }

    public Option<DatabaseDefinition, IDLOptionFailure> ReadSyntaxTrees(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes)
    {
        var syntaxParser = new SyntaxParser(modelSyntaxes);

        // Identify classes implementing the interfaces of interest
        var dbModelClasses = modelSyntaxes
            .Where(cls => cls.BaseList?.Types
                .Any(baseType => baseType.ToString() == "ICustomDatabaseModel" || baseType.ToString() == "IDatabaseModel") == true)
            .ToList();

        // Prioritize the classes implementing ICustomDatabaseModel
        var dbType = dbModelClasses
            .FirstOrDefault(cls => cls.BaseList?.Types
                .Any(baseType => baseType.ToString() == "ICustomDatabaseModel") == true)
            ??
            dbModelClasses.FirstOrDefault();

        var csType = dbType == null
            ? new CsTypeDeclaration("Unnamed", "Unnamed", ModelCsType.Class)
            : new CsTypeDeclaration(MetadataTypeConverter.RemoveInterfacePrefix(dbType.Identifier.Text), CsTypeDeclaration.GetNamespace(dbType), ModelCsType.Class);

        var database = new DatabaseDefinition(csType.Name, csType);

        var customModelClasses = modelSyntaxes
            .Where(cls => cls.BaseList?.Types
                .Any(baseType => baseType.ToString().StartsWith("ICustomTableModel") || baseType.ToString().StartsWith("ICustomViewModel")) == true)
            .ToList();

        if(!customModelClasses
            .Select(cls => syntaxParser.ParseTableModel(database, cls, cls.Identifier.Text))
            .Transpose()
            .TryUnwrap(out var customModels, out var customModelFailures))
            return DLOptionFailure.AggregateFail(customModelFailures);

        var modelClasses = modelSyntaxes
            .Where(cls => cls.BaseList?.Types
                .Any(baseType => baseType.ToString().StartsWith("ITableModel") || baseType.ToString().StartsWith("IViewModel")) ?? true)
            .ToList();

        if (dbType != null)
        {
            if (!dbType.Members.OfType<PropertyDeclarationSyntax>()
                .Where(prop => prop.Type is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
                .Select(prop => syntaxParser.GetTableType(prop, modelClasses))
                .Transpose()
                .FlatMap(x => 
                    x.Select(t => syntaxParser.ParseTableModel(database, t.classSyntax, t.csPropertyName))
                    .Transpose())
                .TryUnwrap(out var models, out var modelFailures))
                return DLOptionFailure.AggregateFail(modelFailures);

            database.SetTableModels(models);


            if (!dbType.AttributeLists
                .SelectMany(attrList => attrList.Attributes)
                .Select(x => syntaxParser.ParseAttribute(x))
                .Transpose()
                .TryUnwrap(out var attributes, out var attrFailures))
                return DLOptionFailure.AggregateFail(attrFailures);

            database.SetAttributes(attributes);
            database.ParseAttributes();
        }
        else
        {
            if (!modelClasses
                .Select(cls => syntaxParser.ParseTableModel(database, cls, cls.Identifier.Text))
                .Transpose()
                .TryUnwrap(out var models, out var modelFailures))
                return DLOptionFailure.AggregateFail(modelFailures);

            database.SetTableModels(models);
        }

        var transformer = new MetadataTransformer(new MetadataTransformerOptions(options.RemoveInterfacePrefix));

        foreach (var customModel in customModels)
        {
            var match = database.TableModels.FirstOrDefault(x => x.Table.DbName == customModel.Table.DbName);

            if (match != null)
                transformer.TransformTable(customModel, match);
            else
                database.SetTableModels(database.TableModels.Concat([customModel]));
        }

        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        if (database.TableModels.Any(x => x.Model.CsType.Name == database.CsType.Name))
            database.SetCsType(database.CsType.MutateName($"{database.CsType.Name}Db"));

        return database;
    }
}