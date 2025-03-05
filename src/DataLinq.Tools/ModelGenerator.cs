using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tools;

public class Generator
{

    protected Action<string> log;

    public Generator(Action<string> log)
    {
        this.log = log;
    }

    protected IEnumerable<Option<string, IDLOptionFailure>> ParseExistingFilesAndDirs(string basePath, List<string> paths)
    {
        foreach (var relativePath in paths)
        {
            // Use Path.Combine to properly join paths instead of manual concatenation
            var srcPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

            // Get the normalized path with correct case
            if (Directory.Exists(srcPath))
            {
                yield return new DirectoryInfo(srcPath).FullName;
            }
            else if (File.Exists(srcPath))
            {
                yield return new FileInfo(srcPath).FullName;
            }
            else
            {
                yield return DLOptionFailure.Fail(DLFailureType.FileNotFound, $"Couldn't find path '{srcPath}'");
            }
        }
    }
}

public enum ModelGeneratorError
{
    UnableToParseSourceFiles
}

public struct ModelGeneratorOptions
{
    public bool ReadSourceModels { get; set; } = false;
    public bool OverwriteExistingModels { get; set; } = false;
    public bool CapitalizeNames { get; set; } = false;
    public bool DeclareEnumsInClass { get; set; } = false;
    public bool SeparateTablesAndViews { get; set; } = false;
    public List<string> Tables { get; set; } = new List<string>();
    public List<string> Views { get; set; } = new List<string>();

    public ModelGeneratorOptions()
    {
    }
}

public class ModelGenerator : Generator
{
    private readonly ModelGeneratorOptions options;

    public ModelGenerator(Action<string> log, ModelGeneratorOptions options) : base(log)
    {
        this.options = options;
    }

    public Option<DatabaseDefinition, IDLOptionFailure> CreateModels(DataLinqDatabaseConnection connection, string basePath, string databaseName)
    {
        var db = connection.DatabaseConfig;

        var sqlOptions = new MetadataFromDatabaseFactoryOptions
        {
            CapitaliseNames = this.options.CapitalizeNames,
            DeclareEnumsInClass = this.options.DeclareEnumsInClass,
            Tables = this.options.Tables,
            Views = this.options.Views
        };

        if (connection.Type == DatabaseType.SQLite && !Path.IsPathRooted(databaseName))
            databaseName = connection.GetRootedPath(basePath); // Path.Combine(basePath, databaseName);

        //var connectionString = connection.ConnectionString;
        //if (connection.Type == DatabaseType.SQLite)
        //    connectionString = $"Data Source={databaseName};Cache=Shared;";

        var connectionString = connection.ConnectionString;
        if (connection.Type == DatabaseType.SQLite)
            connectionString = connectionString.ChangeValue("Data Source", databaseName);

        var fileEncoding = connection.DatabaseConfig.FileEncoding;

        log($"Reading from database: {databaseName}");
        log($"Type: {connection.Type}");

        if (!PluginHook.MetadataFromSqlFactories[connection.Type]
            .GetMetadataFromSqlFactory(sqlOptions)
            .ParseDatabase(db.Name, db.CsType, db.Namespace, databaseName, connectionString.Original)
            .TryUnwrap(out var dbMetadata, out var dbFailure))
            return DLOptionFailure.Fail(dbFailure);

        //var dbMetadata = connection.ParsedType switch
        //{
        //    DatabaseType.MySQL =>
        //        new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, databaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
        //    DatabaseType.SQLite =>
        //        new SQLite.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, databaseName, $"Data Source={databaseName};Cache=Shared;")
        //};

        log($"Tables read from database: {dbMetadata.TableModels.Length}");
        log("");

        var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
        if (this.options.ReadSourceModels)
        {
            if (db.SourceDirectories == null || !db.SourceDirectories.Any())
            {
                log($"No source directory set. Skipping reading of sources.");
            }
            else
            {
                if (!ParseExistingFilesAndDirs(basePath, db.SourceDirectories).Transpose().TryUnwrap(out var srcPathsExists, out var srcPathsFailures))
                    return DLOptionFailure.AggregateFail(srcPathsFailures);

                log($"Reading models from:");
                foreach (var srcPath in srcPathsExists)
                {
                    //if (srcPath.HasFailed)
                    //    return DLOptionFailure.Fail($"Error: {srcPath.Failure}");

                    log($"{srcPath}");
                }

                //var assemblyPathsExists = ParseExistingFilesAndDirs(basePath, db.AssemblyDirectories).ToList();
                //if (assemblyPathsExists.Any())
                //{ 
                //    log($"Reading assemblies from:");
                //    foreach (var path in assemblyPathsExists)
                //        log($"{path}");
                //}

                var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
                if (!new MetadataFromFileFactory(metadataOptions, log).ReadFiles(db.CsType, srcPathsExists).TryUnwrap(out var srcMetadata, out var srcFailure))
                    return srcFailure;
                //if (srcMetadata.HasFailed)
                //{
                //    //log("Error: Unable to parse source files.");
                //    return "Error: Unable to parse source files";
                //}

                log($"Tables in source model files: {srcMetadata.TableModels.Length}");
                log("");

                var transformer = new MetadataTransformer(new MetadataTransformerOptions(db.RemoveInterfacePrefix));
                transformer.TransformDatabase(srcMetadata, dbMetadata);
            }
        }


        var options = new ModelFileFactoryOptions
        {
            NamespaceName = db.Namespace,
            UseRecords = db.UseRecord,
            UseFileScopedNamespaces = db.UseFileScopedNamespaces,
            UseNullableReferenceTypes = db.UseNullableReferenceTypes,
            SeparateTablesAndViews = db.SeparateTablesAndViews
        };

        //log($"Writing models to:");
        foreach (var file in new ModelFileFactory(options).CreateModelFiles(dbMetadata))
        {
            var filepath = $"{destDir}{Path.DirectorySeparatorChar}{file.path}";
            log($"Writing {filepath}");

            if (!File.Exists(filepath))
                Directory.CreateDirectory(Path.GetDirectoryName(filepath));

            File.WriteAllText(filepath, file.contents, fileEncoding);
        }

        return dbMetadata;
    }

}