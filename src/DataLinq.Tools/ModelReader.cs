using DataLinq.Config;
using DataLinq.Metadata;
using System;
using System.Collections.Generic;
using System.IO;

namespace DataLinq.Tools
{
    public class ModelReader
    {
        public Action<string> Log { get; }

        public ModelReader(Action<string> log)
        {
            Log = log;
        }


        public void Read(DataLinqConfig config, string basePath)
        {
            foreach (var database in config.Databases)
            {
                Read(database, basePath);

            }
        }

        public void Read(DataLinqDatabaseConfig db, string basePath)
        {
            Log($"Reading database: {db.Name}");

            //var fileEncoding = db.ParseFileEncoding();

            List<string> dirs = new List<string>();

            if (db.SourceDirectories != null)
                foreach (var dir in db.SourceDirectories)
                    dirs.Add(basePath + Path.DirectorySeparatorChar + dir);

            if (db.DestinationDirectory != null)
                dirs.Add(basePath + Path.DirectorySeparatorChar + db.DestinationDirectory);

            var srcDir = dirs[0];

            if (Directory.Exists(srcDir))
            {
                Log($"Reading models from: {srcDir}");

                var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = db.FileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
                DatabaseMetadata srcMetadata = new MetadataFromFileFactory(metadataOptions, Log).ReadFiles(db.CsType, dirs.ToArray());

                Log($"Tables in model files: {srcMetadata.TableModels.Count}");
            }
            else
            {
                Log($"Couldn't read from SourceDirectory: {srcDir}");
            }

            var sqlOptions = new MetadataFromDatabaseFactoryOptions
            {
                CapitaliseNames = true,
                DeclareEnumsInClass = true,
                Tables = db.Tables,
                Views = db.Views,
            };

            foreach (var connection in db.Connections)
            {
                Log($"Type: {connection.Type}");

                //var databaseName = connection.DatabaseName;
                //string path = null;
                //if (connection.Type == DatabaseType.SQLite)
                //{
                //    if (Path.IsPathRooted(databaseName))

                //    if (Path.IsPathRooted(connection.ConnectionString.Path)

                //    databaseName = Path.Combine(basePath, databaseName);
                //}

                var connectionString = connection.ConnectionString;
                if (connection.Type == DatabaseType.SQLite)
                    connectionString = connectionString.ChangeValue("Data Source", connection.GetRootedPath(basePath)); // $"Data Source={databaseName};Cache=Shared;";

                var dbMetadata = PluginHook.MetadataFromSqlFactories[connection.Type]
                    .GetMetadataFromSqlFactory(sqlOptions)
                    .ParseDatabase(db.Name, db.CsType, connection.DatabaseName, connectionString.Original);

                //var dbMetadata = connection.ParsedType switch
                //{
                //    DatabaseType.MySQL =>
                //        new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, connection.DatabaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
                //    DatabaseType.SQLite =>
                //        new SQLite.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, connection.DatabaseName, connection.ConnectionString)
                //};

                Log($"Name in database: {dbMetadata.DbName}");
                Log($"Tables in database: {dbMetadata.TableModels.Count}");
            }
        }
    }
}