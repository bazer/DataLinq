using DataLinq.Config;
using DataLinq.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataLinq.Tools
{
    public class ModelReader : Generator
    {
        public ModelReader(Action<string> log) : base(log)
        {
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
            log($"Reading database: {db.Name}");

            //var fileEncoding = db.ParseFileEncoding();

            //List<string> dirs = new List<string>();

            //if (db.SourceDirectories != null)
            //    foreach (var dir in db.SourceDirectories)
            //        dirs.Add(basePath + Path.DirectorySeparatorChar + dir);

            var pathsExists = ParseExistingFilesAndDirs(basePath, db.SourceDirectories).ToList();
            log($"Reading models from:");
            foreach (var srcPath in pathsExists)
                log($"{srcPath}");

            if (db.DestinationDirectory != null)
                pathsExists.Add(basePath + Path.DirectorySeparatorChar + db.DestinationDirectory);

            //var assemblyPathsExists = ParseExistingFilesAndDirs(basePath, db.AssemblyDirectories).ToList();
            //if (assemblyPathsExists.Any())
            //{
            //    log($"Reading assemblies from:");
            //    foreach (var path in assemblyPathsExists)
            //        log($"{path}");
            //}

            //var srcDir = dirs[0];

            //if (Directory.Exists(srcDir))
            //{
                //log($"Reading models from: {srcDir}");

                var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = db.FileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
                DatabaseMetadata srcMetadata = new MetadataFromFileFactory(metadataOptions, log).ReadFiles(db.CsType, pathsExists);

                log($"Tables in model files: {srcMetadata.TableModels.Count}");
            //}
            //else
            //{
            //    log($"Couldn't read from SourceDirectory: {srcDir}");
            //}

            var sqlOptions = new MetadataFromDatabaseFactoryOptions
            {
                CapitaliseNames = true,
                DeclareEnumsInClass = true,
                Tables = db.Tables,
                Views = db.Views,
            };

            foreach (var connection in db.Connections)
            {
                log($"Type: {connection.Type}");

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

                DatabaseMetadata dbMetadata = PluginHook.MetadataFromSqlFactories[connection.Type]
                    .GetMetadataFromSqlFactory(sqlOptions)
                    .ParseDatabase(db.Name, db.CsType, connection.DatabaseName, connectionString.Original);

                //var dbMetadata = connection.ParsedType switch
                //{
                //    DatabaseType.MySQL =>
                //        new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, connection.DatabaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
                //    DatabaseType.SQLite =>
                //        new SQLite.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, connection.DatabaseName, connection.ConnectionString)
                //};

                log($"Name in database: {dbMetadata.DbName}");
                log($"Tables in database: {dbMetadata.TableModels.Count}");
            }
        }
    }
}