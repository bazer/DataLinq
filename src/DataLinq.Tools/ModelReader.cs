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
        LogIgnoredSourceDirectories(db);

        var modelDirectoryPath = GetModelDirectoryPath(db, basePath);
        if (!Directory.Exists(modelDirectoryPath))
            return DLOptionFailure.Fail(DLFailureType.FileNotFound, $"Couldn't find model directory '{modelDirectoryPath}'");

        var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = db.FileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
        if (!ParseDatabaseDefinitionFromFiles(metadataOptions, db.CsType, [modelDirectoryPath]).TryUnwrap(out var srcMetadata, out var srcFailure))
            return srcFailure;

        log("Reading models from:");
        log(new DirectoryInfo(modelDirectoryPath).FullName);
        log($"Tables in model files: {srcMetadata.TableModels.Length}");

        var sqlOptions = new MetadataFromDatabaseFactoryOptions
        {
            CapitaliseNames = true,
            DeclareEnumsInClass = true,
            Include = db.Include,
            Log = log
        };

        foreach (var connection in db.Connections)
        {
            log($"Type: {connection.Type}");

            var connectionString = connection.ConnectionString;
            if (connection.Type == DatabaseType.SQLite)
                connectionString = connectionString.ChangeValue("Data Source", connection.GetRootedPath(basePath));

            DatabaseDefinition dbMetadata = PluginHook.MetadataFromSqlFactories[connection.Type]
                .GetMetadataFromSqlFactory(sqlOptions)
                .ParseDatabase(db.Name, db.CsType, db.Namespace, connection.DataSourceName, connectionString.Original);

            log($"Name in database: {dbMetadata.DbName}");
            log($"Tables read from database: {dbMetadata.TableModels.Length}");
        }

        return true;
    }
}
