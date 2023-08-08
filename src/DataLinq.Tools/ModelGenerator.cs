using DataLinq.Config;
using DataLinq.Metadata;
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

        public Option<DatabaseMetadata, ModelGeneratorError> Create(DataLinqDatabaseConnection connection, string basePath, string databaseName)
        {
            var db = connection.DatabaseConfig;

            var sqlOptions = new MetadataFromDatabaseFactoryOptions
            {
                CapitaliseNames = this.options.CapitalizeNames,
                DeclareEnumsInClass = this.options.DeclareEnumsInClass
            };

            if (connection.Type == DatabaseType.SQLite && !Path.IsPathRooted(databaseName))
                databaseName = connection.GetRootedPath(basePath); // Path.Combine(basePath, databaseName);

            //var connectionString = connection.ConnectionString;
            //if (connection.Type == DatabaseType.SQLite)
            //    connectionString = $"Data Source={databaseName};Cache=Shared;";

            var connectionString = connection.ConnectionString;
            if (connection.Type == DatabaseType.SQLite)
                connectionString = connectionString.ChangeValue("Data Source", databaseName);

            var fileEncoding = connection.DatabaseConfig.FileEncoding;

            log($"Reading from database: {databaseName}");
            log($"Type: {connection.Type}");

            var dbMetadata = PluginHook.MetadataFromSqlFactories[connection.Type]
                .GetMetadataFromSqlFactory(sqlOptions)
                .ParseDatabase(db.Name, db.CsType, databaseName, connectionString.Original);

            //var dbMetadata = connection.ParsedType switch
            //{
            //    DatabaseType.MySQL =>
            //        new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, databaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
            //    DatabaseType.SQLite =>
            //        new SQLite.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, databaseName, $"Data Source={databaseName};Cache=Shared;")
            //};

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
                    var srcPaths = db.SourceDirectories.Select(path => basePath + Path.DirectorySeparatorChar + path);

                    foreach (var srcPath in srcPaths.Where(x => !Directory.Exists(x) && !File.Exists(x)))
                    {
                        log($"Couldn't find dir: {srcPath}");
                    }

                    var srcPathsExists = srcPaths.Where(x => Directory.Exists(x) || File.Exists(x)).ToArray();

                    log($"Reading models from:");
                    foreach (var srcPath in srcPathsExists)
                    {
                        log($"{srcPath}");
                    }

                    var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix ?? false };
                    var srcMetadata = new MetadataFromFileFactory(metadataOptions, log).ReadFiles(db.CsType, srcPathsExists);
                    if (srcMetadata.HasFailed)
                    {
                        log("Error: Unable to parse source files.");
                        return ModelGeneratorError.UnableToParseSourceFiles;
                    }

                    log($"Tables in source model files: {srcMetadata.Value.TableModels.Count}");

                    var transformer = new MetadataTransformer(new MetadataTransformerOptions(db.RemoveInterfacePrefix ?? false));
                    transformer.TransformDatabase(srcMetadata, dbMetadata);
                }
            }

            log($"Writing models to: {db.DestinationDirectory}");

            var options = new FileFactoryOptions
            {
                NamespaceName = db.Namespace ?? "Models",
                UseRecords = db.UseRecord ?? true,
                UseCache = db.UseCache ?? true,
                SeparateTablesAndViews = db.SeparateTablesAndViews ?? false
            };

            foreach (var file in new FileFactory(options).CreateModelFiles(dbMetadata))
            {
                var filepath = $"{destDir}{Path.DirectorySeparatorChar}{file.path}";
                log($"Writing {filepath}");

                if (!File.Exists(filepath))
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                File.WriteAllText(filepath, file.contents, fileEncoding);
            }

            return dbMetadata;
        }
    }
}