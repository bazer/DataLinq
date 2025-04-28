using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.ErrorHandling;
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

    public Option<List<DatabaseDefinition>, IDLOptionFailure> ReadFiles(string csType, IEnumerable<string> srcPaths) => DLOptionFailure.CatchAll<List<DatabaseDefinition>>(() =>
    {
        var trees = new List<SyntaxTree>();

        foreach (var path in srcPaths)
        {
            try
            {
                // Process directories
                if (Directory.Exists(path))
                {
                    var directoryInfo = new DirectoryInfo(path);
                    var files = directoryInfo.EnumerateFiles("*.cs", SearchOption.AllDirectories);

                    foreach (var fileInfo in files)
                    {
                        // Use FileInfo.FullName to get the correct case-preserved path
                        var fullPath = fileInfo.FullName;
                        var sourceText = File.ReadAllText(fullPath, this.options.FileEncoding);

                        var syntaxTree = CSharpSyntaxTree.ParseText(
                            sourceText,
                            CSharpParseOptions.Default,
                            path: fullPath // Use the case-preserved file path
                        );

                        trees.Add(syntaxTree);
                        //Log?.Invoke($"Parsed file: {fullPath}");
                    }
                }
                // Process individual files
                else if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    var fullPath = fileInfo.FullName; // Get proper case
                    var sourceText = File.ReadAllText(fullPath, this.options.FileEncoding);

                    var syntaxTree = CSharpSyntaxTree.ParseText(
                        sourceText,
                        CSharpParseOptions.Default,
                        path: fullPath // Use the case-preserved file path
                    );

                    trees.Add(syntaxTree);
                    //Log?.Invoke($"Parsed file: {fullPath}");
                }
                else
                {
                    return DLOptionFailure.Fail(DLFailureType.FileNotFound, $"Path not found: {path}");
                }
            }
            catch (Exception ex)
            {
                return DLOptionFailure.Fail(DLFailureType.FileNotFound, $"Error processing path '{path}': {ex.Message}");
            }
        }

        var modelSyntaxes = trees
            .Where(x => x.HasCompilationUnitRoot)
            .SelectMany(x => x.GetCompilationUnitRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Where(cls => cls.BaseList?.Types.Any(baseType =>
                        SyntaxParser.IsModelInterface(baseType.ToString())) == true))
            .ToImmutableArray();

        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions
        {
            RemoveInterfacePrefix = options.RemoveInterfacePrefix,
            FileEncoding = options.FileEncoding
        }, Log);

        if (!factory
            .ReadSyntaxTrees(modelSyntaxes)
            .Transpose()
            .TryUnwrap(out var models, out var modelFailures))
            return DLOptionFailure.AggregateFail(modelFailures);

        return models;
        //return ReadSyntaxTrees(modelSyntaxes);
    });

    //private Option<DatabaseDefinition, IDLOptionFailure> ReadSyntaxTrees(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes)
    //{
    //    var syntaxParser = new SyntaxParser(modelSyntaxes);

    //    // Identify classes implementing the interfaces of interest
    //    var dbType = modelSyntaxes
    //        .Where(cls => cls.BaseList?.Types
    //            .Any(baseType => baseType.ToString() == "IDatabaseModel") == true)
    //        .FirstOrDefault();

    //    var csType = dbType == null
    //        ? new CsTypeDeclaration("Unnamed", "Unnamed", ModelCsType.Class)
    //        : new CsTypeDeclaration(MetadataTypeConverter.RemoveInterfacePrefix(dbType.Identifier.Text), CsTypeDeclaration.GetNamespace(dbType), ModelCsType.Class);

    //    var database = new DatabaseDefinition(csType.Name, csType);

    //    if (!string.IsNullOrEmpty(dbType?.SyntaxTree.FilePath))
    //        database.SetCsFile(new CsFileDeclaration(dbType!.SyntaxTree.FilePath));

    //    var modelClasses = modelSyntaxes
    //        .Where(cls => cls.BaseList?.Types
    //            .Any(baseType => baseType.ToString().StartsWith("ITableModel") || baseType.ToString().StartsWith("IViewModel")) ?? true)
    //        .ToList();

    //    if (dbType != null)
    //    {
    //        if (!dbType.Members.OfType<PropertyDeclarationSyntax>()
    //            .Where(prop => prop.Type is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
    //            .Select(prop => syntaxParser.GetTableType(prop, modelClasses))
    //            .Transpose()
    //            .FlatMap(x => 
    //                x.Select(t => syntaxParser.ParseTableModel(database, t.classSyntax, t.csPropertyName))
    //                .Transpose())
    //            .TryUnwrap(out var models, out var modelFailures))
    //            return DLOptionFailure.AggregateFail(modelFailures);

    //        database.SetTableModels(models);


    //        if (!dbType.AttributeLists
    //            .SelectMany(attrList => attrList.Attributes)
    //            .Select(x => syntaxParser.ParseAttribute(x))
    //            .Transpose()
    //            .TryUnwrap(out var attributes, out var attrFailures))
    //            return DLOptionFailure.AggregateFail(attrFailures);

    //        database.SetAttributes(attributes);
    //        database.ParseAttributes();
    //    }
    //    else
    //    {
    //        if (!modelClasses
    //            .Select(cls => syntaxParser.ParseTableModel(database, cls, cls.Identifier.Text))
    //            .Transpose()
    //            .TryUnwrap(out var models, out var modelFailures))
    //            return DLOptionFailure.AggregateFail(modelFailures);

    //        database.SetTableModels(models);
    //    }

    //    var transformer = new MetadataTransformer(new MetadataTransformerOptions(options.RemoveInterfacePrefix));

    //    MetadataFactory.ParseIndices(database);
    //    MetadataFactory.ParseRelations(database);

    //    if (database.TableModels.Any(x => x.Model.CsType.Name == database.CsType.Name))
    //        database.SetCsType(database.CsType.MutateName($"{database.CsType.Name}Db"));

    //    return database;
    //}
}