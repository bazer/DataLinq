using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools.Config;
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


        public void Read(ConfigFile config, string basePath)
        {
            foreach (var database in config.Databases)
            {
                Read(database, basePath);

            }
        }

        public void Read(DatabaseConfig db, string basePath)
        {
            Log($"Reading database: {db.Name}");

            List<string> dirs = new List<string>();
            dirs.Add(basePath + Path.DirectorySeparatorChar + db.SourceDirectories);

            if (db.DestinationDirectory != null)
                dirs.Add(basePath + Path.DirectorySeparatorChar + db.DestinationDirectory);

            var srcDir = dirs[0];

            if (Directory.Exists(srcDir))
            {
                Log($"Reading models from: {srcDir}");
                DatabaseMetadata srcMetadata = new MetadataFromFileFactory(Log).ReadFiles(db.CsType, dirs.ToArray());

                Log($"Tables in model files: {srcMetadata.TableModels.Count}");
            }
            else
            {
                Log($"Couldn't read from SourceDirectory: {srcDir}");
            }

            var sqlOptions = new MetadataFromSqlFactoryOptions
            {
                CapitaliseNames = true,
                DeclareEnumsInClass = true
            };

            foreach (var connection in db.Connections)
            {
                Log($"Type: {connection.ParsedType}");

                var dbMetadata = connection.ParsedType switch
                {
                    DatabaseType.MySQL =>
                        new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, connection.DatabaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
                    DatabaseType.SQLite =>
                        SQLite.MetadataFromSqlFactory.ParseDatabase(db.Name, connection.DatabaseName, connection.ConnectionString)
                };

                Log($"Name in database: {dbMetadata.DbName}");
                Log($"Tables in database: {dbMetadata.TableModels.Count}");
            }
        }
    }
}