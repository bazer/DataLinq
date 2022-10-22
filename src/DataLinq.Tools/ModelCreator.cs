using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools.Config;
using System;
using System.IO;
using System.Text;

namespace DataLinq.Tools
{
    public struct ModelCreatorOptions
    {
        public bool ReadSourceModels { get; set; }
        public bool OverwriteExistingModels { get; set; }
    }

    public class ModelCreator
    {
        private readonly ModelCreatorOptions options;

        private Action<string> log;

        public ModelCreator(Action<string> log, ModelCreatorOptions options)
        {
            this.log = log;
            this.options = options;
        }

        public void Create(DatabaseConfig db, DatabaseConnectionConfig connection, string basePath)
        {
            log($"Reading from database: {db.Name}");
            log($"Type: {connection.Type}");

            var dbMetadata = connection.ParsedType switch
            {
                DatabaseType.MySQL =>
                    MySql.MetadataFromSqlFactory.ParseDatabase(db.Name, connection.DatabaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
                DatabaseType.SQLite =>
                    SQLite.MetadataFromSqlFactory.ParseDatabase(db.Name, connection.DatabaseName, connection.ConnectionString)
            };

            log($"Tables in database: {dbMetadata.Tables.Count}");

            var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
            if (options.ReadSourceModels)
            {
                var srcDir = basePath + Path.DirectorySeparatorChar + db.SourceDirectory;
                if (Directory.Exists(srcDir))
                {
                    log($"Reading models from:");
                    log($"{srcDir}");
                    log($"{destDir}");
                    var srcMetadata = new MetadataFromFileFactory(log).ReadFiles(db.CsType, srcDir, destDir);

                    if (srcMetadata != null)
                    {
                        log($"Tables in source model files: {srcMetadata.Tables.Count}");

                        var transformer = new MetadataTransformer(log, new MetadataTransformerOptions(true));
                        transformer.Transform(srcMetadata, dbMetadata);
                    }
                }
                else
                {
                    log($"Couldn't read from SourceDirectory: {srcDir}");
                    return;
                }
            }

            log($"Writing models to: {db.DestinationDirectory}");

            var settings = new FileFactorySettings
            {
                NamespaceName = db.Namespace ?? "Models",
                UseRecords = db.UseRecord ?? true,
                UseCache = db.UseCache ?? true
            };

            foreach (var file in FileFactory.CreateModelFiles(dbMetadata, settings))
            {
                var filepath = $"{destDir}{Path.DirectorySeparatorChar}{file.path}";
                log($"Writing {filepath}");

                if (!File.Exists(filepath))
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                File.WriteAllText(filepath, file.contents, Encoding.UTF8);
            }
        }
    }
}