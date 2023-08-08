using DataLinq.Config;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Query;
using DataLinq.SQLite;
using System;
using System.IO;
using System.Text;
using ThrowAway;

namespace DataLinq.Tools
{
    public enum SqlGeneratorError
    {
        DestDirectoryNotFound,
        UnableToParseModelFiles,
        CouldNotGenerateSql
    }

    public struct SqlGeneratorOptions
    {
    }

    public class SqlGenerator
    {
        private readonly SqlGeneratorOptions options;

        private Action<string> log;

        static SqlGenerator()
        {
            MySQLProvider.RegisterProvider();
            SQLiteProvider.RegisterProvider();
        }

        public SqlGenerator(Action<string> log, SqlGeneratorOptions options)
        {
            this.log = log;
            this.options = options;
        }

        public Option<Sql, SqlGeneratorError> Create(DataLinqDatabaseConnection connection, string basePath, string path)
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

            var options = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix ?? false };
            var dbMetadata = new MetadataFromFileFactory(options, log).ReadFiles(db.CsType, destDir);
            if (dbMetadata.HasFailed)
            {
                log("Error: Unable to parse model files.");
                return SqlGeneratorError.UnableToParseModelFiles;
            }

            log($"Tables in model files: {dbMetadata.Value.TableModels.Count}");
            log($"Writing sql to: {path}");

            var sql = PluginHook.GenerateSql(connection.Type, dbMetadata, true);

            if (sql.HasFailed)
            {
                log(sql.Failure.ToString());
                return SqlGeneratorError.CouldNotGenerateSql;
            }

            File.WriteAllText(path, sql.Value.Text, Encoding.UTF8);

            return sql.Value;
        }
    }
}