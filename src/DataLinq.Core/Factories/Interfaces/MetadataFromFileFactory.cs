using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
//using ThrowAway;

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

    //public Option<DatabaseMetadata, MetadataFromFileFactoryError> ReadFiles(string csType, IEnumerable<string> srcPaths)
    public DatabaseMetadata ReadFiles(string csType, IEnumerable<string> srcPaths)
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
                    .Where(cls => cls.BaseList?.Types.Any(baseType => IsModelInterface(baseType.ToString()) || IsCustomModelInterface(baseType.ToString())) == true))
            .ToList();

        return ReadSyntaxTrees(modelSyntaxes);
    }

    public DatabaseMetadata ReadSyntaxTrees(List<TypeDeclarationSyntax> modelSyntaxes)
    {
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

        var database = new DatabaseMetadata(MetadataTypeConverter.RemoveInterfacePrefix(dbType?.Identifier.Text ?? "Unnamed"));

        if (dbType != null)
            database.CsNamespace = GetNamespace(dbType);

        var customModelClasses = modelSyntaxes
            .Where(cls => cls.BaseList?.Types
                .Any(baseType => baseType.ToString().StartsWith("ICustomTableModel") || baseType.ToString().StartsWith("ICustomViewModel")) == true)
            .ToList();

        var customModels = customModelClasses
            .Select(cls => ParseTableModel(database, cls, cls.Identifier.Text))
            .ToList();


        var modelClasses = modelSyntaxes
            .Where(cls => cls.BaseList?.Types
                .Any(baseType => baseType.ToString().StartsWith("ITableModel") || baseType.ToString().StartsWith("IViewModel")) ?? true)
            .ToList();

        if (dbType != null)
        {
            database.TableModels = dbType.Members.OfType<PropertyDeclarationSyntax>()
                .Where(prop => prop.Type is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
                .Select(prop => GetTableType(prop, modelClasses))
                .Select(t => ParseTableModel(database, t.classSyntax, t.csPropertyName))
                .ToList();

            database.Attributes = dbType.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(x => ParseAttribute(x)).ToArray();
            database.ParseAttributes();
        }
        else
        {
            database.TableModels = modelClasses
                .Select(cls => ParseTableModel(database, cls, cls.Identifier.Text))
                .ToList();
        }

        var transformer = new MetadataTransformer(new MetadataTransformerOptions(options.RemoveInterfacePrefix));

        foreach (var customModel in customModels)
        {
            var match = database.TableModels.FirstOrDefault(x => x.Table.DbName == customModel.Table.DbName);

            if (match != null)
                transformer.TransformTable(customModel, match);
            else
                database.TableModels.Add(customModel);
        }


        //if (dbType != null)
        //{
        //    database.Attributes = dbType.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(x => ParseAttribute(x)).ToArray();
        //    database.ParseAttributes();
        //}

        MetadataFactory.ParseIndices(database);
        MetadataFactory.ParseRelations(database);

        if (database.TableModels.Any(x => x.Model.CsTypeName == database.CsTypeName))
            database.CsTypeName = $"{database.CsTypeName}Db";

        return database;
    }

    public static string GetNamespace(TypeDeclarationSyntax typeSyntax)
    {
        // Traverse up the syntax tree to find the containing namespace
        SyntaxNode? potentialNamespaceParent = typeSyntax.Parent;

        while (potentialNamespaceParent != null && !(potentialNamespaceParent is NamespaceDeclarationSyntax || potentialNamespaceParent is FileScopedNamespaceDeclarationSyntax))
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        // If we found a NamespaceDeclarationSyntax, return its name
        if (potentialNamespaceParent is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            return namespaceDeclaration.Name.ToString();
        }
        else if (potentialNamespaceParent is FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration)
        {
            return fileScopedNamespaceDeclaration.Name.ToString();
        }

        // If no namespace was found, return an empty string or some default value
        return string.Empty;
    }

    private static readonly string[] modelInterfaceNames = ["IDatabaseModel", "ITableModel", "IViewModel"];
    private static readonly string[] customModelInterfaceName = ["ICustomDatabaseModel", "ICustomTableModel", "ICustomViewModel"];

    public static bool IsModelInterface(string interfaceName) =>
        modelInterfaceNames.Any(interfaceName.StartsWith);

    public static bool IsCustomModelInterface(string interfaceName) =>
        customModelInterfaceName.Any(interfaceName.StartsWith);

    private TableModelMetadata ParseTableModel(DatabaseMetadata database, TypeDeclarationSyntax typeSyntax, string csPropertyName)
    {
        var model = typeSyntax == null
            ? new ModelMetadata
            {
                Database = database,
                ModelCsType = ModelCsType.Interface,
                CsTypeName = csPropertyName,
                Interfaces = [],
                Attributes = []
            }
             : ParseModel(database, typeSyntax);

        return new TableModelMetadata
        {
            IsStub = typeSyntax == null,
            Model = model,
            Table = ParseTable(model),
            CsPropertyName = csPropertyName
        };
    }

    private (string csPropertyName, TypeDeclarationSyntax classSyntax) GetTableType(PropertyDeclarationSyntax property, List<TypeDeclarationSyntax> modelTypeSyntaxes)
    {
        var propType = property.Type;

        if (propType is GenericNameSyntax genericType && genericType.Identifier.Text == "DbRead")
        {
            var typeArgument = genericType.TypeArgumentList.Arguments[0] as IdentifierNameSyntax;
            if (typeArgument != null)
            {
                var modelClass = modelTypeSyntaxes.FirstOrDefault(cls => cls.Identifier.Text == typeArgument.Identifier.Text);
                return (property.Identifier.Text, modelClass);
            }
        }
        throw new NotImplementedException();
    }


    private ModelMetadata ParseModel(DatabaseMetadata database, TypeDeclarationSyntax typeSyntax)
    {
        var model = new ModelMetadata
        {
            Database = database,
            ModelCsType = ParseModelCsType(typeSyntax),
            CsTypeName = typeSyntax.Identifier.Text,
            CsNamespace = GetNamespace(typeSyntax),
            Attributes = typeSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes).Select(x => ParseAttribute(x)).ToArray(),
            Interfaces = typeSyntax.BaseList?.Types.Select(baseType => new ModelTypeDeclaration { CsTypeName = baseType.ToString() }).ToArray() ?? []
        };

        if (model.ModelCsType == ModelCsType.Interface)
            model.CsTypeName = MetadataTypeConverter.RemoveInterfacePrefix(model.CsTypeName);

        typeSyntax.Members.OfType<PropertyDeclarationSyntax>()
            .Where(prop => prop.AttributeLists.SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString() == "Column" || attr.Name.ToString() == "Relation"))
            .Select(prop => ParseProperty(prop, model))
        .ToList()
        .ForEach(model.AddProperty);

        model.Usings = typeSyntax.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(uds => uds?.Name?.ToString())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .OrderBy(ns => ns!.StartsWith("System"))
            .ThenBy(ns => ns)
            .Select(ns => new ModelUsing { FullNamespaceName = ns! })
            .ToArray();

        return model;
    }

    private ModelCsType ParseModelCsType(TypeDeclarationSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            ClassDeclarationSyntax => ModelCsType.Class,
            RecordDeclarationSyntax => ModelCsType.Record,
            InterfaceDeclarationSyntax => ModelCsType.Interface,
            _ => throw new NotImplementedException($"Unknown type of TypeDeclarationSyntax '{typeSyntax}'"),
        };
    }

    private Attribute ParseAttribute(AttributeSyntax attributeSyntax)
    {
        var name = attributeSyntax.Name.ToString();
        var arguments = attributeSyntax.ArgumentList?.Arguments
            .Select(x => x.Expression.ToString().Trim('"'))
            .ToList() ?? new();



        if (name == "Database")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new DatabaseAttribute(arguments[0]);
        }

        if (name == "Table")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new TableAttribute(arguments[0]);
        }

        if (name == "View")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new ViewAttribute(arguments[0]);
        }

        if (name == "Column")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new ColumnAttribute(arguments[0]);
        }

        if (name == "Definition")
        {
            if (arguments.Count != 1)
                throw new ArgumentException($"Attribute '{name}' doesn't have any arguments");

            return new DefinitionAttribute(arguments[0]);
        }

        if (name == "UseCache")
        {
            if (arguments.Count == 1)
                return new UseCacheAttribute(bool.Parse(arguments[0]));
            else
                return new UseCacheAttribute();
        }

        if (name == "CacheLimit")
        {
            if (arguments.Count != 2)
                throw new ArgumentException($"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheLimitType limitType))
                throw new ArgumentException($"Invalid CacheLimitType value '{arguments[0]}'");

            return new CacheLimitAttribute(limitType, long.Parse(arguments[1]));
        }

        if (name == "CacheCleanup")
        {
            if (arguments.Count != 2)
                throw new ArgumentException($"Attribute '{name}' doesn't have 2 arguments");

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out CacheCleanupType cleanupType))
                throw new ArgumentException($"Invalid CacheCleanupType value '{arguments[0]}'");

            return new CacheCleanupAttribute(cleanupType, long.Parse(arguments[1]));
        }

        if (name == "IndexCache")
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                throw new ArgumentException($"Attribute '{name}' doesn't have 1 or 2 arguments");
            }

            if (!Enum.TryParse(arguments[0].Split('.').Last(), out IndexCacheType indexCacheType))
            {
                throw new ArgumentException($"Invalid IndexCacheType value '{arguments[0]}'");
            }

            return arguments.Count == 1
                ? new IndexCacheAttribute(indexCacheType)
                : new IndexCacheAttribute(indexCacheType, int.Parse(arguments[1]));
        }

        if (name == "AutoIncrement")
        {
            if (arguments.Any())
                throw new ArgumentException($"Attribute '{name}' have too many arguments");

            return new AutoIncrementAttribute();
        }

        if (name == "Relation")
        {
            if (arguments.Count == 2)
                return new RelationAttribute(arguments[0], arguments[1]);
            else if (arguments.Count == 3)
                return new RelationAttribute(arguments[0], arguments[1], arguments[2]);
            else
                throw new ArgumentException($"Attribute '{name}' doesn't have 2 or 3 arguments");
        }

        if (name == "PrimaryKey")
        {
            if (arguments.Any())
                throw new ArgumentException($"Attribute '{name}' have too many arguments");

            return new PrimaryKeyAttribute();
        }

        if (name == "ForeignKey")
        {
            if (arguments.Count != 3)
                throw new ArgumentException($"Attribute '{name}' must have 3 arguments");

            return new ForeignKeyAttribute(arguments[0], arguments[1], arguments[2]);
        }

        if (name == "Enum")
        {
            return new EnumAttribute(arguments.ToArray());
        }

        if (name == "Nullable")
        {
            if (arguments.Any())
                throw new ArgumentException($"Attribute '{name}' have too many arguments");

            return new NullableAttribute();
        }

        if (name == "Index")
        {
            if (arguments.Count < 2)
                throw new ArgumentException($"Attribute '{name}' have too few arguments");

            string indexName = arguments[0];
            if (!Enum.TryParse(arguments[1].Split('.').Last(), out IndexCharacteristic characteristic))
                throw new ArgumentException($"Invalid IndexCharacteristic value '{arguments[1]}'");

            if (arguments.Count == 2)
                return new IndexAttribute(arguments[0], characteristic);

            if (Enum.TryParse(arguments[2].Split('.').Last(), out IndexType type))
                return new IndexAttribute(indexName, characteristic, type, arguments.Skip(3).ToArray());
            else
                return new IndexAttribute(indexName, characteristic, arguments.Skip(2).ToArray());
        }


        if (name == "Type")
        {
            string enumValue = arguments[0].Split('.').Last();
            if (Enum.TryParse(enumValue, out DatabaseType dbType))
            {
                switch (arguments.Count)
                {
                    case 1: throw new ArgumentException($"Attribute '{name}' have too few arguments");
                    case 2: return new TypeAttribute(dbType, arguments[1]);
                    case 3:
                        if (long.TryParse(arguments[2], out long length))
                            return new TypeAttribute(dbType, arguments[1], length);
                        else
                            return new TypeAttribute(dbType, arguments[1], bool.Parse(arguments[2]));
                    case 4:
                        if (int.TryParse(arguments[3], out int decimals))
                            return new TypeAttribute(dbType, arguments[1], long.Parse(arguments[2]), decimals);
                        else
                            return new TypeAttribute(dbType, arguments[1], long.Parse(arguments[2]), bool.Parse(arguments[3]));
                    case 5:
                        return new TypeAttribute(dbType, arguments[1], long.Parse(arguments[2]), int.Parse(arguments[3]), bool.Parse(arguments[4]));
                }
            }
            else
            {
                switch (arguments.Count)
                {
                    case 1: return new TypeAttribute(arguments[0]);
                    case 2:
                        if (long.TryParse(arguments[1], out long length))
                            return new TypeAttribute(arguments[0], length);
                        else
                            return new TypeAttribute(arguments[0], bool.Parse(arguments[1]));
                    case 3:
                        return new TypeAttribute(arguments[0], long.Parse(arguments[1]), bool.Parse(arguments[2]));
                    case 4:
                        return new TypeAttribute(arguments[0], long.Parse(arguments[1]), int.Parse(arguments[2]), bool.Parse(arguments[3]));
                }
            }

            throw new NotImplementedException($"Attribute 'TypeAttribute' with {arguments.Count} arguments not implemented");
        }

        throw new NotImplementedException($"Attribute '{name}' not implemented");
    }

    private TableMetadata ParseTable(ModelMetadata model)
    {
        TableMetadata table;

        if (model.Interfaces.Any(x => x.CsTypeName.StartsWith("ITableModel") || x.CsTypeName.StartsWith("ICustomTableModel")))
        {
            table = new TableMetadata();
        }
        else
        {
            table = new ViewMetadata();
        }

        table.Model = model;
        table.Database = model.Database;
        table.DbName = model.CsTypeName;

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.DbName = tableAttribute.Name;

            if (attribute is UseCacheAttribute useCache)
                table.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (table is ViewMetadata view && attribute is DefinitionAttribute definitionAttribute)
                view.Definition = definitionAttribute.Sql;
        }

        table.Columns = model.ValueProperties.Values
            .Select(x => table.ParseColumn(x))
            .ToArray();

        model.Table = table;

        return table;
    }

    private Property ParseProperty(PropertyDeclarationSyntax propSyntax, ModelMetadata model)
    {
        var attributes = propSyntax.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Select(attrSyntax => ParseAttribute(attrSyntax))
            .ToList();

        var property = GetProperty(attributes);

        property.Model = model;
        property.CsName = propSyntax.Identifier.Text;
        property.CsTypeName = propSyntax.Type.ToString().Trim('?', '"', '\"');
        property.Attributes = attributes;

        if (property is ValueProperty valueProp)
        {
            valueProp.CsNullable = propSyntax.Type is NullableTypeSyntax;

            if (attributes.Any(attribute => attribute is EnumAttribute))
            {
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize("enum");

                var enumValueList = attributes.OfType<EnumAttribute>().Single().Values.Select((x, i) => (x, i + 1)).ToList();
                valueProp.EnumProperty = new EnumProperty(enumValueList, null, true);
            }
            else
            {
                valueProp.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            }
        }

        return property;
    }

    private Property GetProperty(List<Attribute> attributes)
    {
        if (attributes.Any(attr => attr is RelationAttribute))
            return new RelationProperty();

        return new ValueProperty();
    }
}


//private static IEnumerable<MetadataReference> GetReferences(params Type[] types)
//{
//    foreach (var type in types)
//        yield return MetadataReference.CreateFromFile(type.Assembly.Location);
//}

//private static IEnumerable<MetadataReference> GetReferences(params string[] paths)
//{
//    foreach (var path in paths)
//    {
//        if (File.Exists(path))
//        {
//            if (IsAssembly(path))
//            {
//                yield return MetadataReference.CreateFromFile(path);
//            }
//        }
//        else if (Directory.Exists(path))
//        {
//            foreach (var file in Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories)) // assuming assemblies have ".dll" extension
//            {
//                if (IsAssembly(file))
//                {
//                    yield return MetadataReference.CreateFromFile(file);
//                }
//            }
//        }
//        //else
//        //{
//        //    // Handle invalid path case.
//        //    // Maybe log an error or throw an exception.
//        //}
//    }
//}

//private static bool IsAssembly(string filePath)
//{
//    // You can either check by file extension:
//    // return Path.GetExtension(filePath).Equals(".dll", StringComparison.OrdinalIgnoreCase);

//    // Or try to load the assembly and catch any exception:
//    try
//    {
//        AssemblyName.GetAssemblyName(filePath);
//        return true;
//    }
//    catch
//    {
//        return false;
//    }
//}

//public Option<DatabaseMetadata, MetadataFromFileFactoryError> ReadFiles(string csType, List<string> srcPaths, List<string> assemblyPaths)
//{
//    var trees = new List<SyntaxTree>();

//    foreach (var path in srcPaths)
//    {
//        var sourceFiles = Directory.Exists(path)
//            ? new DirectoryInfo(path)
//                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
//                .Select(a => a.FullName)
//            : new FileInfo(path).FullName.Yield();

//        foreach (string file in sourceFiles)
//            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file, options.FileEncoding)));
//    }

//    var references = GetReferences(
//        typeof(object),
//        typeof(System.Runtime.DependentHandle),
//        typeof(System.Collections.ObjectModel.ObservableCollection<object>),
//        typeof(System.ComponentModel.DesignerCategoryAttribute),
//        typeof(System.ComponentModel.DataAnnotations.AssociatedMetadataTypeTypeDescriptionProvider),
//        typeof(System.Xml.Serialization.CodeGenerationOptions),
//        typeof(Newtonsoft.Json.ConstructorHandling),
//        typeof(Remotion.Linq.DefaultQueryProvider),
//        typeof(System.Linq.EnumerableExecutor),
//        typeof(System.Linq.Expressions.BinaryExpression),
//        typeof(DataLinq),
//        typeof(SyntaxTree),
//        typeof(CSharpSyntaxTree))
//        .ToList();

//    references.AddRange(GetReferences(assemblyPaths.ToArray()));

//    references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));
//    references.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location));

//    var compilation = CSharpCompilation.Create("datalinq_metadata.dll",
//       trees,
//       references,
//       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

//    using (var ms = new MemoryStream())
//    {
//        var result = compilation.Emit(ms);

//        if (!result.Success)
//        {
//            var failures = result.Diagnostics.Where(diagnostic =>
//                diagnostic.IsWarningAsError ||
//                diagnostic.Severity == DiagnosticSeverity.Error);

//            foreach (Diagnostic diagnostic in failures)
//            {
//                Log($"{diagnostic.Id}: {diagnostic.GetMessage()}");
//            }

//            return MetadataFromFileFactoryError.CompilationError;
//        }

//        ms.Seek(0, SeekOrigin.Begin);

//        //Assembly assembly = Assembly.ReflectionOnlyLoadFrom(ms.ToArray());

//        Assembly assembly = Assembly.Load(ms.ToArray());

//        //List<Type> dbTypes = assembly.ExportedTypes.Where(x => x.Name == csType)
//        //    .Concat(assembly.ExportedTypes.Where(x => x.Name == "I" + csType))
//        //    .ToList();



//        var types = assembly.ExportedTypes.Where(x =>
//            x.GetInterface("ICustomDatabaseModel") != null ||
//            x.GetInterface("IDatabaseModel") != null ||
//            x.GetInterface("ICustomTableModel") != null ||
//            x.GetInterface("ICustomViewModel") != null)
//            .ToArray();

//        if (types.Length == 0)
//        {
//            Log($"Couldn't find any type '{csType}' that implements 'IDatabaseModel', 'ICustomDatabaseModel', 'ICustomTableModel' or 'ICustomViewModel'");
//            return MetadataFromFileFactoryError.TypeNotFound;
//        }

//        return MetadataFromInterfaceFactory.ParseDatabaseFromSources(options.RemoveInterfacePrefix, types);
//    }
//}
//}
