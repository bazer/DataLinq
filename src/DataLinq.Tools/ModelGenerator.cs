using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
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

    protected Option<DatabaseDefinition, IDLOptionFailure> ParseDatabaseDefinitionFromFiles(MetadataFromFileFactoryOptions options, string csType, IEnumerable<string> srcPaths)
    {
        if (!new MetadataFromFileFactory(options, log).ReadFiles(csType, srcPaths).TryUnwrap(out var srcMetadata, out var srcFailure))
            return srcFailure;

        if (srcMetadata == null || srcMetadata.Count == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No database found in model files. Please check the model files.");

        if (srcMetadata.Count == 1 && srcMetadata[0].TableModels.Length == 0)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"No tables found in model files. Please check the model files.");

        if (srcMetadata.Count > 1)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"More than one database found in model files. Found the following databases:\n {srcMetadata.Select(x => x.Name).ToJoinedString()}");

        return srcMetadata[0];
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
                    log($"{srcPath}");

                var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
                if (!ParseDatabaseDefinitionFromFiles(metadataOptions, db.CsType, srcPathsExists).TryUnwrap(out var srcMetadata, out var srcFailure))
                    return srcFailure;

                if (srcMetadata.Name != db.Name)
                    return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Database name in model files does not match the database name. Expected: {db.Name}, Found: {srcMetadata.Name}");

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