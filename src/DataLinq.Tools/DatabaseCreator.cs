using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.Tools.Config;
using System;
using System.IO;
using System.Text;
using ThrowAway;

namespace DataLinq.Tools
{
    public enum DatabaseCreatorError
    {
        DestDirectoryNotFound,
        UnableToParseModelFiles,
        CouldNotCreateDatabase
    }

    public struct DatabaseCreatorOptions
    {
    }

    public class DatabaseCreator
    {
        private readonly DatabaseCreatorOptions options;

        private Action<string> log;

        static DatabaseCreator()
        {
            MySQLProvider.RegisterProvider();
            SQLiteProvider.RegisterProvider();
        }

        public DatabaseCreator(Action<string> log, DatabaseCreatorOptions options)
        {
            this.log = log;
            this.options = options;
        }

        public Option<int, DatabaseCreatorError> Create(DatabaseConfig db, DatabaseConnectionConfig connection, string basePath, string databaseName)
        {
            log($"Type: {connection.Type}");
            

            var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
            if (!Directory.Exists(destDir))
            {
                log($"Couldn't find dir: {destDir}");
                return DatabaseCreatorError.DestDirectoryNotFound;
            }

            var dbMetadata = new MetadataFromFileFactory(log).ReadFiles(db.CsType, destDir);
            if (dbMetadata.HasFailed)
            {
                log("Error: Unable to parse model files.");
                return DatabaseCreatorError.UnableToParseModelFiles;
            }

            log($"Tables in model files: {dbMetadata.Value.TableModels.Count}");

            if (connection.ParsedType == DatabaseType.SQLite && !Path.IsPathRooted(databaseName))
                databaseName = Path.Combine(basePath, databaseName);

            log($"Creating database '{databaseName}'");

            var sql = PluginHook.CreateDatabaseFromMetadata(connection.ParsedType.Value, dbMetadata, databaseName, connection.ConnectionString, true);

            if (sql.HasFailed)
            {
                log(sql.Failure.ToString());
                return DatabaseCreatorError.CouldNotCreateDatabase;
            }

            return sql.Value;
        }
    }
}