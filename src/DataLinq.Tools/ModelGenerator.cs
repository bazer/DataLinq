using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools.Config;
using System;
using System.IO;
using System.Linq;
using System.Text;
using ThrowAway;

namespace DataLinq.Tools
{
    public enum ModelGeneratorError
    {
        UnableToParseSourceFiles
    }

    public struct ModelGeneratorOptions
    {
        public bool ReadSourceModels { get; set; }
        public bool OverwriteExistingModels { get; set; }
        public bool CapitalizeNames { get; set; }
        public bool DeclareEnumsInClass { get; set; }
        public bool SeparateTablesAndViews { get; set; }
    }

    public class ModelGenerator
    {
        private readonly ModelGeneratorOptions options;

        private Action<string> log;

        public ModelGenerator(Action<string> log, ModelGeneratorOptions options)
        {
            this.log = log;
            this.options = options;
        }

        public Option<DatabaseMetadata, ModelGeneratorError> Create(DatabaseConfig db, DatabaseConnectionConfig connection, string basePath)
        {
            log($"Reading from database: {db.Name}");
            log($"Type: {connection.Type}");

            var sqlOptions = new MetadataFromSqlFactoryOptions
            {
                CapitaliseNames = this.options.CapitalizeNames,
                DeclareEnumsInClass = this.options.DeclareEnumsInClass
            };

            var dbMetadata = connection.ParsedType switch
            {
                DatabaseType.MySQL =>
                    new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, connection.DatabaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
                DatabaseType.SQLite =>
                    SQLite.MetadataFromSqlFactory.ParseDatabase(db.Name, connection.DatabaseName, connection.ConnectionString)
            };

            log($"Tables in database: {dbMetadata.TableModels.Count}");

            var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
            if (this.options.ReadSourceModels)
            {
                if (db.SourceDirectories == null)
                {
                    log($"No source directory set. Skipping reading of sources.");
                }
                else
                {
                    var srcDirs = db.SourceDirectories.Select(dir => basePath + Path.DirectorySeparatorChar + dir);

                    foreach (var srcDir in srcDirs.Where(x => !Directory.Exists(x)))
                    {
                        log($"Couldn't find dir: {srcDir}");
                    }

                    var srcDirsExists = srcDirs.Where(x => Directory.Exists(x)).ToArray();

                    log($"Reading models from:");
                    foreach (var srcDir in srcDirsExists)
                    {
                        log($"{srcDir}");
                    }

                    var srcMetadata = new MetadataFromFileFactory(log).ReadFiles(db.CsType, srcDirsExists);
                    if (srcMetadata.HasFailed)
                    {
                        log("Error: Unable to parse source files.");
                        return ModelGeneratorError.UnableToParseSourceFiles;
                    }

                    log($"Tables in source model files: {srcMetadata.Value.TableModels.Count}");

                    var transformer = new MetadataTransformer(log, new MetadataTransformerOptions(true));
                    transformer.Transform(srcMetadata, dbMetadata);
                }
            }

            log($"Writing models to: {db.DestinationDirectory}");

            var options = new FileFactoryOptions
            {
                NamespaceName = db.Namespace ?? "Models",
                UseRecords = db.UseRecord ?? true,
                UseCache = db.UseCache ?? true,
                SeparateTablesAndViews = db.SeparateTablesAndViews ?? false,
            };

            foreach (var file in new FileFactory(options).CreateModelFiles(dbMetadata))
            {
                var filepath = $"{destDir}{Path.DirectorySeparatorChar}{file.path}";
                log($"Writing {filepath}");

                if (!File.Exists(filepath))
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                File.WriteAllText(filepath, file.contents, Encoding.UTF8);
            }

            return dbMetadata;
        }
    }
}