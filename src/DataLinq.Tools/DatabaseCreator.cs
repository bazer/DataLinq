﻿using System;
using System.IO;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tools;

public enum DatabaseCreatorError
{
    DestDirectoryNotFound,
    UnableToParseModelFiles,
    CouldNotCreateDatabase
}

public struct DatabaseCreatorOptions
{
}


public class DatabaseCreator : Generator
{
    private readonly DatabaseCreatorOptions options;

    static DatabaseCreator()
    {
        MySQLProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();
    }

    public DatabaseCreator(Action<string> log, DatabaseCreatorOptions options) : base(log)
    {
        this.options = options;
    }

    public Option<int, IDLOptionFailure> Create(DataLinqDatabaseConnection connection, string basePath, string databaseName)
    {
        log($"Type: {connection.Type}");

        var db = connection.DatabaseConfig;
        var fileEncoding = db.FileEncoding;

        var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
        if (!Directory.Exists(destDir))
        {
            log($"Couldn't find dir: {destDir}");
            return DLOptionFailure.Fail(DatabaseCreatorError.DestDirectoryNotFound);
        }

        var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
        if (!ParseDatabaseDefinitionFromFiles(metadataOptions, db.CsType, destDir.Yield().ToList()).TryUnwrap(out var dbMetadata, out var metaDataFailure))
            return metaDataFailure;

        log($"Tables in model files: {dbMetadata.TableModels.Length}");

        if (connection.Type == DatabaseType.SQLite && !Path.IsPathRooted(databaseName))
            databaseName = Path.Combine(basePath, databaseName);

        log($"Creating database '{databaseName}'");

        var sql = PluginHook.CreateDatabaseFromMetadata(connection.Type, dbMetadata, databaseName, connection.ConnectionString.Original, true);

        if (sql.HasFailed)
        {
            log(sql.Failure.ToString());
            return DLOptionFailure.Fail(DatabaseCreatorError.CouldNotCreateDatabase);
        }

        return sql.Value;
    }
}