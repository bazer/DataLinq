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
    public List<string> Include { get; set; } = [];
    public bool OverwritePropertyTypes { get; set; } = false;
    public GeneratedFileStamp? GeneratedFileStamp { get; set; }

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
            Include = this.options.Include,
            Log = log
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
            return dbFailure;

        log($"Tables read from database: {dbMetadata.TableModels.Length}");
        log("");

        var destDir = Path.GetFullPath(Path.Combine(basePath, db.DestinationDirectory ?? ""));
        var sourceRelationPropertyKeys = new HashSet<string>(StringComparer.Ordinal);
        var sourceModelsApplied = false;
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

                sourceRelationPropertyKeys = GetRelationPropertyKeys(srcMetadata);

                log($"Tables in source model files: {srcMetadata.TableModels.Length}");
                log("");

                var transformerOptions = new MetadataTransformerOptions(
                    removeInterfacePrefix: db.RemoveInterfacePrefix,
                    overwritePropertyTypes: this.options.OverwritePropertyTypes
                );
                var transformer = new MetadataTransformer(transformerOptions);
                dbMetadata = transformer.TransformDatabaseSnapshot(srcMetadata, dbMetadata);
                sourceModelsApplied = true;
            }
        }

        LogGeneratedRelationFallbackWarnings(dbMetadata, sourceRelationPropertyKeys);

        var options = new ModelFileFactoryOptions
        {
            NamespaceName = sourceModelsApplied ? null : db.Namespace,
            UseRecords = db.UseRecord,
            UseFileScopedNamespaces = db.UseFileScopedNamespaces,
            UseNullableReferenceTypes = db.UseNullableReferenceTypes,
            SeparateTablesAndViews = db.SeparateTablesAndViews,
            GeneratedFileStamp = this.options.GeneratedFileStamp
        };

        List<(string path, string contents)> modelFiles;
        try
        {
            modelFiles = new ModelFileFactory(options)
                .CreateModelFiles(dbMetadata)
                .Select(file => (Path.GetFullPath(Path.Combine(destDir, file.path)), file.contents))
                .ToList();
        }
        catch (Exception exception)
        {
            return CreateModelFileRenderingFailure(exception);
        }

        var writeResult = SafeGeneratedFileWriter.WriteAll(modelFiles, fileEncoding, log);
        if (writeResult.HasFailed)
            return writeResult.Failure;

        return dbMetadata;
    }

    private static IDLOptionFailure CreateModelFileRenderingFailure(Exception exception)
    {
        if (exception is ModelFileGenerationException modelFileGenerationException)
        {
            var sourceLocation = modelFileGenerationException.GetSourceLocation();
            if (sourceLocation.HasValue)
            {
                return DLOptionFailure.Fail(
                    DLFailureType.Exception,
                    modelFileGenerationException.Message,
                    sourceLocation.Value);
            }
        }

        return DLOptionFailure.Fail(
            DLFailureType.Exception,
            $"Failed to render generated model files. {exception.Message}");
    }

    private void LogGeneratedRelationFallbackWarnings(
        DatabaseDefinition database,
        IReadOnlySet<string> sourceRelationPropertyKeys)
    {
        foreach (var relationProperty in database.TableModels
            .Where(tableModel => !tableModel.IsStub)
            .SelectMany(tableModel => tableModel.Model.RelationProperties.Values))
        {
            if (TryGetRelationPropertyKey(relationProperty, out var relationPropertyKey) &&
                sourceRelationPropertyKeys.Contains(relationPropertyKey))
            {
                continue;
            }

            if (!MetadataFactory.TryGetGeneratedRelationPropertyFallback(
                relationProperty,
                out var preferredPropertyName,
                out var existingPropertyKind))
            {
                continue;
            }

            var relation = relationProperty.RelationPart.Relation;
            log(
                $"Warning: Foreign key '{relation.ConstraintName}' on table '{relation.ForeignKey.ColumnIndex.Table.DbName}' would generate relation property '{relationProperty.Model.CsType.Name}.{preferredPropertyName}', but model '{relationProperty.Model.CsType.Name}' already defines a {existingPropertyKind} with that name. Generated relation property '{relationProperty.Model.CsType.Name}.{relationProperty.PropertyName}' instead. Add an explicit [Relation] property with a non-conflicting name to control this.");
        }
    }

    private static HashSet<string> GetRelationPropertyKeys(DatabaseDefinition database)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationProperty in database.TableModels
            .Where(tableModel => !tableModel.IsStub)
            .SelectMany(tableModel => tableModel.Model.RelationProperties.Values))
        {
            if (TryGetRelationPropertyKey(relationProperty, out var key))
                keys.Add(key);
        }

        return keys;
    }

    private static bool TryGetRelationPropertyKey(RelationProperty relationProperty, out string key)
    {
        key = string.Empty;
        if (relationProperty.RelationPart is null)
            return false;

        var part = relationProperty.RelationPart;
        var otherSide = part.GetOtherSide();
        key =
            $"{otherSide.ColumnIndex.Table.DbName}.({FormatColumnNames(otherSide.ColumnIndex.Columns)})->" +
            $"{part.ColumnIndex.Table.DbName}.({FormatColumnNames(part.ColumnIndex.Columns)})";
        return true;
    }

    private static string FormatColumnNames(IReadOnlyList<ColumnDefinition> columns) =>
        string.Join(",", columns.Select(static column => column.DbName));

}
