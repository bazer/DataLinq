using System;
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

public class ModelReader : Generator
{
    public ModelReader(Action<string> log) : base(log)
    {
    }

    public Option<bool, IDLOptionFailure> Read(DataLinqConfig config, string basePath)
    {
        foreach (var database in config.Databases)
        {
            if (!Read(database, basePath).TryUnwrap(out var success, out var failure))
                return failure;
        }

        return true;
    }

    public Option<bool, IDLOptionFailure> Read(DataLinqDatabaseConfig db, string basePath)
    {
        log($"Reading database: {db.Name}");

        if (!ParseExistingFilesAndDirs(basePath, db.SourceDirectories)
            .ToList()
            .Transpose()
            .TryUnwrap(out var paths, out var failures))
            return DLOptionFailure.AggregateFail(failures);

        log($"Reading models from:");
        foreach (var srcPath in paths)
            log(srcPath);

        if (db.DestinationDirectory != null)
            paths.Add(basePath + Path.DirectorySeparatorChar + db.DestinationDirectory);

        var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = db.FileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
        if (!ParseDatabaseDefinitionFromFiles(metadataOptions, db.CsType, paths).TryUnwrap(out var srcMetadata, out var srcFailure))
            return srcFailure;

        log($"Tables in model files: {srcMetadata.TableModels.Length}");

        var sqlOptions = new MetadataFromDatabaseFactoryOptions
        {
            CapitaliseNames = true,
            DeclareEnumsInClass = true,
            Include = db.Include
        };

        foreach (var connection in db.Connections)
        {
            log($"Type: {connection.Type}");

            var connectionString = connection.ConnectionString;
            if (connection.Type == DatabaseType.SQLite)
                connectionString = connectionString.ChangeValue("Data Source", connection.GetRootedPath(basePath)); // $"Data Source={databaseName};Cache=Shared;";

            DatabaseDefinition dbMetadata = PluginHook.MetadataFromSqlFactories[connection.Type]
                .GetMetadataFromSqlFactory(sqlOptions)
                .ParseDatabase(db.Name, db.CsType, db.Namespace, connection.DataSourceName, connectionString.Original);

            log($"Name in database: {dbMetadata.DbName}");
            log($"Tables read from database: {dbMetadata.TableModels.Length}");
        }

        return true;
    }
}