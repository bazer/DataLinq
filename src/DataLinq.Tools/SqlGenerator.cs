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
    public enum SqlGeneratorError
    {
        DestDirectoryNotFound,
        UnableToParseModelFiles
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

        public Option<Sql, SqlGeneratorError> Create(DatabaseConfig db, DatabaseConnectionConfig connection, string basePath, string path)
        {
            log($"Type: {connection.Type}");
            

            var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
            if (!Directory.Exists(destDir))
            {
                log($"Couldn't find dir: {destDir}");
                return SqlGeneratorError.DestDirectoryNotFound;
            }

            var dbMetadata = new MetadataFromFileFactory(log).ReadFiles(db.CsType, destDir);
            if (dbMetadata.HasFailed)
            {
                log("Error: Unable to parse model files.");
                return SqlGeneratorError.UnableToParseModelFiles;
            }

            log($"Tables in model files: {dbMetadata.Value.TableModels.Count}");
            log($"Writing sql to: {path}");

            var sql = DatabaseCreator.GenerateSql(connection.ParsedType.Value, dbMetadata, true);

            File.WriteAllText(path, sql.Text, Encoding.UTF8);

            return sql;
        }
    }
}