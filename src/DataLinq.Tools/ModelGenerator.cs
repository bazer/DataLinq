using DataLinq.Config;
using DataLinq.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThrowAway;

namespace DataLinq.Tools
{
    public class Generator
    {

        protected Action<string> log;

        public Generator(Action<string> log)
        {
            this.log = log;
        }

        protected IEnumerable<string> ParseExistingFilesAndDirs(string basePath, List<string> paths)
        {
            foreach (var srcPath in paths.Select(path => basePath + Path.DirectorySeparatorChar + path))
            {
                if (!Directory.Exists(srcPath) && !File.Exists(srcPath))
                    log($"Couldn't find path: {srcPath}");
                else
                    yield return srcPath;
            }
        }
    }

    public enum ModelGeneratorError
    {
        UnableToParseSourceFiles
    }

    public struct ModelGeneratorOptions
    {
        public bool ReadSourceModels { get; set; } = false;
        public bool OverwriteExistingModels { get; set; } = false;
        public bool CapitalizeNames { get; set; } = false;
        public bool DeclareEnumsInClass { get; set; } = false;
        public bool SeparateTablesAndViews { get; set; } = false;
        public List<string> Tables { get; set; } = new List<string>();
        public List<string> Views { get; set; } = new List<string>();

        public ModelGeneratorOptions()
        {
        }
    }

    public class ModelGenerator : Generator
    {
        private readonly ModelGeneratorOptions options;

        public ModelGenerator(Action<string> log, ModelGeneratorOptions options) : base(log)
        {
            this.options = options;
        }

        public Option<DatabaseMetadata> CreateModels(DataLinqDatabaseConnection connection, string basePath, string databaseName)
        {
            var db = connection.DatabaseConfig;

            var sqlOptions = new MetadataFromDatabaseFactoryOptions
            {
                CapitaliseNames = this.options.CapitalizeNames,
                DeclareEnumsInClass = this.options.DeclareEnumsInClass,
                Tables = this.options.Tables,
                Views = this.options.Views
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

            if (dbMetadata.HasFailed)
                return dbMetadata.Failure;

            //var dbMetadata = connection.ParsedType switch
            //{
            //    DatabaseType.MySQL =>
            //        new MySql.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, databaseName, new MySqlDatabase<information_schema>(connection.ConnectionString, "information_schema").Query()),
            //    DatabaseType.SQLite =>
            //        new SQLite.MetadataFromSqlFactory(sqlOptions).ParseDatabase(db.Name, db.CsType, databaseName, $"Data Source={databaseName};Cache=Shared;")
            //};

            log($"Tables in database: {dbMetadata.Value.TableModels.Count}");

            var destDir = basePath + Path.DirectorySeparatorChar + db.DestinationDirectory;
            if (this.options.ReadSourceModels)
            {
                if (db.SourceDirectories == null || !db.SourceDirectories.Any())
                {
                    log($"No source directory set. Skipping reading of sources.");
                }
                else
                {
                    var srcPathsExists = ParseExistingFilesAndDirs(basePath, db.SourceDirectories).ToList();
                    log($"Reading models from:");
                    foreach (var srcPath in srcPathsExists)
                        log($"{srcPath}");

                    //var assemblyPathsExists = ParseExistingFilesAndDirs(basePath, db.AssemblyDirectories).ToList();
                    //if (assemblyPathsExists.Any())
                    //{ 
                    //    log($"Reading assemblies from:");
                    //    foreach (var path in assemblyPathsExists)
                    //        log($"{path}");
                    //}

                    var metadataOptions = new MetadataFromFileFactoryOptions { FileEncoding = fileEncoding, RemoveInterfacePrefix = db.RemoveInterfacePrefix };
                    var srcMetadata = new MetadataFromFileFactory(metadataOptions, log).ReadFiles(db.CsType, srcPathsExists);
                    if (srcMetadata.HasFailed)
                    {
                        log("Error: Unable to parse source files.");
                        return "Unable to parse source files";
                    }

                    log($"Tables in source model files: {srcMetadata.Value.TableModels.Count}");

                    var transformer = new MetadataTransformer(new MetadataTransformerOptions(db.RemoveInterfacePrefix));
                    transformer.TransformDatabase(srcMetadata, dbMetadata);
                }
            }

            log($"Writing models to: {db.DestinationDirectory}");

            var options = new FileFactoryOptions
            {
                NamespaceName = db.Namespace,
                UseRecords = db.UseRecord,
                //UseCache = db.UseCache,
                UseFileScopedNamespaces = db.UseFileScopedNamespaces,
                SeparateTablesAndViews = db.SeparateTablesAndViews
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