﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools.Config;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

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

            var srcDir = basePath + Path.DirectorySeparatorChar + db.SourceDirectory;
            if (Directory.Exists(srcDir))
            {
                Log($"Reading models from: {srcDir}");
                var srcMetadata = new MetadataFromFileFactory(Log).ReadFiles(srcDir, db.CsType);

                Log($"Tables in model files: {srcMetadata.Tables.Count}");
            }
            else
            {
                Log($"Couldn't read from SourceDirectory: {srcDir}");
            }

            foreach (var connection in db.Connections)
            {
                Log($"Type: {connection.ParsedType}");

                var dbMetadata = connection.ParsedType switch
                {
                    DatabaseType.MySQL =>
                        MySql.MetadataFromSqlFactory.ParseDatabase(connection.DatabaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
                    DatabaseType.SQLite =>
                        SQLite.MetadataFromSqlFactory.ParseDatabase(connection.DatabaseName, connection.ConnectionString)
                };

                Log($"Name in database: {dbMetadata.Name}");
                Log($"Tables in database: {dbMetadata.Tables.Count}");
            }
        }
    }
}