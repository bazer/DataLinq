using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

public class MetadataFromInterfacesFactory
{
    private readonly MetadataFromInterfacesFactoryOptions options;
    public Action<string>? Log { get; }

    public MetadataFromInterfacesFactory(MetadataFromInterfacesFactoryOptions options, Action<string>? log = null)
    {
        this.options = options;
        Log = log;
    }

    public IEnumerable<DatabaseDefinition> ReadFiles(string csType, IEnumerable<string> srcPaths)
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
                    .Where(cls => cls.BaseList?.Types.Any(baseType => SyntaxParser.IsModelInterface(baseType.ToString())) == true))
            .ToImmutableArray();

        return ReadSyntaxTrees(modelSyntaxes);
    }

    public IEnumerable<DatabaseDefinition> ReadSyntaxTrees(ImmutableArray<TypeDeclarationSyntax> modelSyntaxes)
    {
        // Identify classes implementing the interfaces of interest
        var dbModelClasses = modelSyntaxes
            .Where(cls => cls.BaseList != null && cls.BaseList.Types
                .Any(baseType => baseType.ToString() == "IDatabaseModel"))
            .ToList();

        foreach (var dbType in dbModelClasses)
        {
            if (dbType == null)
                throw new ArgumentException("Database model class not found");

            if (dbType.Identifier.Text == null)
                throw new ArgumentException("Database model class must have a name");

            var name = MetadataTypeConverter.RemoveInterfacePrefix(dbType.Identifier.Text);
            var database = new DatabaseDefinition(name, new CsTypeDeclaration(dbType));

            var modelClasses = modelSyntaxes
                .Where(cls => cls.BaseList != null && cls.BaseList.Types
                    .Any(baseType => (baseType.ToString().StartsWith("ITableModel") || baseType.ToString().StartsWith("IViewModel"))))
                .Where(cls => cls.BaseList != null && cls.BaseList.Types
                    .Any(baseType => baseType.ToString().Contains($"<{dbType.Identifier.Text}>")))
                .ToList();

            database.SetTableModels(dbType.Members.OfType<PropertyDeclarationSyntax>()
                .Where(prop => prop.Type is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
                .Select(prop => SyntaxParser.GetTableType(prop, modelClasses))
                .Select(t => SyntaxParser.ParseTableModel(database, t.classSyntax, t.csPropertyName)));

            database.SetAttributes(dbType.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(x => SyntaxParser.ParseAttribute(x)));
            database.ParseAttributes();

            MetadataFactory.ParseIndices(database);
            MetadataFactory.ParseRelations(database);

            if (database.TableModels.Any(x => x.CsPropertyName == database.CsType.Name))
                database.SetCsType(database.CsType.MutateName($"{database.CsType.Name}Db"));

            yield return database;
        }
    }
}