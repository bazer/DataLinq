using System;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Config;
using DataLinq.Extensions;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Query;
using DataLinq.SQLite;
using ThrowAway;

namespace DataLinq.Tools;

public enum SqlGeneratorError
{
    DestDirectoryNotFound,
    UnableToParseModelFiles,
    CouldNotGenerateSql
}

public struct SqlGeneratorOptions
{
}

public class SqlGenerator : Generator
{
    private readonly SqlGeneratorOptions options;

    static SqlGenerator()
    {
        MySQLProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();
    }

    public SqlGenerator(Action<string> log, SqlGeneratorOptions options) : base(log)
    {
        this.options = options;
    }

    public Option<Sql, SqlGeneratorError> Create(DataLinqDatabaseConnection connection, string basePath, string writePath)
    {
        log($"Type: {connection.Type}");

        var db = connection.DatabaseConfig;
        var fileEncoding = db.FileEncoding;

        var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
        if (!Directory.Exists(destDir))
        {
            log($"Couldn't find dir: {destDir}");
            return SqlGeneratorError.DestDirectoryNotFound;
        }

        //var assemblyPathsExists = ParseExistingFilesAndDirs(basePath, db.AssemblyDirectories).ToList();
        //if (assemblyPathsExists.Any())
        //{
        //    log($"Reading assemblies from:");
        //    foreach (var assemblyPath in assemblyPathsExists)
        //        log($"{assemblyPath}");
        //}

        var options = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
        var dbMetadata = new MetadataFromFileFactory(options, log).ReadFiles(db.CsType, destDir.Yield().ToList());
        if (dbMetadata.HasFailed)
        {
            log("Error: Unable to parse model files.");
            return SqlGeneratorError.UnableToParseModelFiles;
        }

        log($"Tables in model files: {dbMetadata.Value.TableModels.Count}");
        log($"Writing sql to: {writePath}");

        var sql = PluginHook.GenerateSql(connection.Type, dbMetadata, true);

        if (sql.HasFailed)
        {
            log(sql.Failure.ToString());
            return SqlGeneratorError.CouldNotGenerateSql;
        }

        File.WriteAllText(writePath, sql.Value.Text, Encoding.UTF8);

        return sql.Value;
    }
}