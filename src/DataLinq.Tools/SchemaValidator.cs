using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Validation;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tools;

public class SchemaValidator : Generator
{
    static SchemaValidator()
    {
        MySQLProvider.RegisterProvider();
        MariaDBProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();
    }

    public SchemaValidator(Action<string> log) : base(log)
    {
    }

    public Option<SchemaValidationRunResult, IDLOptionFailure> Validate(
        DataLinqDatabaseConnection connection,
        string basePath,
        string? databaseName)
    {
        var db = connection.DatabaseConfig;
        var modelPaths = GetModelPaths(db, basePath).ToList();
        if (!modelPaths.Transpose().TryUnwrap(out var paths, out var pathFailures))
            return DLOptionFailure.AggregateFail(pathFailures);

        var metadataOptions = new MetadataFromFileFactoryOptions
        {
            FileEncoding = db.FileEncoding,
            RemoveInterfacePrefix = db.RemoveInterfacePrefix
        };
        if (!ParseDatabaseDefinitionFromFiles(metadataOptions, db.CsType, paths).TryUnwrap(out var modelMetadata, out var modelFailure))
            return modelFailure;

        var resolvedDatabaseName = databaseName ?? connection.DataSourceName ?? db.Name;
        var connectionString = connection.ConnectionString;
        if (connection.Type == DatabaseType.SQLite)
        {
            if (!Path.IsPathRooted(resolvedDatabaseName))
                resolvedDatabaseName = connection.GetRootedPath(basePath);

            connectionString = connectionString.ChangeValue("Data Source", resolvedDatabaseName);
        }

        var databaseOptions = new MetadataFromDatabaseFactoryOptions
        {
            CapitaliseNames = db.CapitalizeNames,
            DeclareEnumsInClass = true,
            Include = db.Include,
            Log = log
        };

        if (!PluginHook.MetadataFromSqlFactories[connection.Type]
            .GetMetadataFromSqlFactory(databaseOptions)
            .ParseDatabase(db.Name, db.CsType, db.Namespace, resolvedDatabaseName, connectionString.Original)
            .TryUnwrap(out var databaseMetadata, out var databaseFailure))
            return DLOptionFailure.Fail(databaseFailure);

        var differences = SchemaComparer.Compare(modelMetadata, databaseMetadata, connection.Type);
        return new SchemaValidationRunResult(
            db.Name,
            connection.Type,
            resolvedDatabaseName,
            modelMetadata.TableModels.Length,
            databaseMetadata.TableModels.Length,
            differences);
    }

    private static IEnumerable<Option<string, IDLOptionFailure>> GetModelPaths(DataLinqDatabaseConfig db, string basePath)
    {
        foreach (var path in GetExistingPaths(basePath, db.SourceDirectories))
            yield return path;

        if (!string.IsNullOrWhiteSpace(db.DestinationDirectory))
        {
            var destinationPath = Path.GetFullPath(Path.Combine(basePath, db.DestinationDirectory));
            if (Directory.Exists(destinationPath))
                yield return new DirectoryInfo(destinationPath).FullName;
            else
                yield return DLOptionFailure.Fail(DLFailureType.FileNotFound, $"Couldn't find path '{destinationPath}'");
        }
    }

    private static IEnumerable<Option<string, IDLOptionFailure>> GetExistingPaths(string basePath, IEnumerable<string> paths)
    {
        foreach (var relativePath in paths)
        {
            var path = Path.GetFullPath(Path.Combine(basePath, relativePath));
            if (Directory.Exists(path))
                yield return new DirectoryInfo(path).FullName;
            else if (File.Exists(path))
                yield return new FileInfo(path).FullName;
            else
                yield return DLOptionFailure.Fail(DLFailureType.FileNotFound, $"Couldn't find path '{path}'");
        }
    }
}

public sealed class SchemaValidationRunResult
{
    public SchemaValidationRunResult(
        string databaseName,
        DatabaseType databaseType,
        string dataSourceName,
        int modelTableCount,
        int databaseTableCount,
        IReadOnlyList<SchemaDifference> differences)
    {
        DatabaseName = databaseName;
        DatabaseType = databaseType;
        DataSourceName = dataSourceName;
        ModelTableCount = modelTableCount;
        DatabaseTableCount = databaseTableCount;
        Differences = differences;
    }

    public string DatabaseName { get; }
    public DatabaseType DatabaseType { get; }
    public string DataSourceName { get; }
    public int ModelTableCount { get; }
    public int DatabaseTableCount { get; }
    public IReadOnlyList<SchemaDifference> Differences { get; }
    public bool HasDifferences => Differences.Count > 0;
}
