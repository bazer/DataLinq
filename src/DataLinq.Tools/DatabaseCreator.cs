using System;
using System.IO;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories.Models;
using DataLinq.Extensions;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using ThrowAway;

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

    public Option<int, DatabaseCreatorError> Create(DataLinqDatabaseConnection connection, string basePath, string databaseName)
    {
        log($"Type: {connection.Type}");

        var db = connection.DatabaseConfig;
        var fileEncoding = db.FileEncoding;

        var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
        if (!Directory.Exists(destDir))
        {
            log($"Couldn't find dir: {destDir}");
            return DatabaseCreatorError.DestDirectoryNotFound;
        }

        //var assemblyPathsExists = ParseExistingFilesAndDirs(basePath, db.AssemblyDirectories).ToList();
        //if (assemblyPathsExists.Any())
        //{
        //    log($"Reading assemblies from:");
        //    foreach (var path in assemblyPathsExists)
        //        log($"{path}");
        //}

        var options = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
        var dbMetadata = new MetadataFromFileFactory(options, log).ReadFiles(db.CsType, destDir.Yield().ToList());
        //if (dbMetadata.HasFailed)
        //{
        //    log("Error: Unable to parse model files.");
        //    return DatabaseCreatorError.UnableToParseModelFiles;
        //}

        log($"Tables in model files: {dbMetadata.TableModels.Count}");

        if (connection.Type == DatabaseType.SQLite && !Path.IsPathRooted(databaseName))
            databaseName = Path.Combine(basePath, databaseName);

        log($"Creating database '{databaseName}'");

        var sql = PluginHook.CreateDatabaseFromMetadata(connection.Type, dbMetadata, databaseName, connection.ConnectionString.Original, true);

        if (sql.HasFailed)
        {
            log(sql.Failure.ToString());
            return DatabaseCreatorError.CouldNotCreateDatabase;
        }

        return sql.Value;
    }
}