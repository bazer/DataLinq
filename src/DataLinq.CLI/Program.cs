using CommandLine;
using DataLinq.Tools;
using DataLinq.Tools.Config;
using ThrowAway;

namespace DataLinq.CLI
{
    static class Program
    {
        [Verb("create-database", HelpText = "Create selected database")]
        public class CreateDatabaseOptions : CreateOptions
        {
        }

        [Verb("create-sql", HelpText = "Create SQL for selected database")]
        public class CreateSqlOptions : CreateOptions
        {
            [Option('o', "output", HelpText = "Path to output file", Required = true)]
            public string OutputFile { get; set; }
        }

        [Verb("create-models", HelpText = "Create models for selected database")]
        public class CreateModelsOptions : CreateOptions
        {
            [Option('s', "skip-source", HelpText = "Skip reading from source models", Required = false)]
            public bool SkipSource { get; set; }
        }

        public class CreateOptions : Options
        {
            [Option('d', "database-name", HelpText = "Database name on server", Required = false)]
            public string DatabaseName { get; set; }

            [Option('n', "name", HelpText = "Schema name", Required = true)]
            public string SchemaName { get; set; }

            [Option('t', "type", HelpText = "Which database connection type to create the database for", Required = false)]
            public string ConnectionType { get; set; }
        }


        [Verb("list", HelpText = "List all databases in config.")]
        public class ListOptions : Options
        {
        }

        public class Options
        {
            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }

            [Option('c', "config", Required = false, HelpText = "Path to config file")]
            public string ConfigPath { get; set; }
        }

        static bool Verbose;
        static string ConfigPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json";
        static ConfigFile ConfigFile;
        static string ConfigBasePath => Path.GetDirectoryName(ConfigPath);

        static public bool ReadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                Console.WriteLine($"Couldn't find config file 'datalinq.json'. Tried searching path:");
                Console.WriteLine(ConfigPath);
                return false;
            }

            ConfigFile = ConfigReader.Read(ConfigPath);

            return true;
        }

        static private Option<(DatabaseConfig db, DatabaseConnectionConfig connection)> GetConnection(string dbName, string connectionType)
        {
            var db = ConfigFile.Databases.SingleOrDefault(x => x.Name.ToLower() == dbName.ToLower());
            if (db == null)
            {
                return $"Couldn't find database with name '{dbName}'";
            }

            if (db.Connections.Count == 0)
            {
                return $"Database '{dbName}' has no connections to read from";
            }

            if (db.Connections.Count > 1 && connectionType == null)
            {
                return $"Database '{dbName}' has more than one connection to read from, you need to select which one";
            }

            DatabaseConnectionConfig connection = null;
            if (connectionType != null)
            {
                connection = db.Connections.SingleOrDefault(x => x.Type.ToLower() == connectionType.ToLower());

                if (connection == null)
                {
                    return $"Couldn't find connection with type '{connectionType}' in configuration file.";
                }
            }

            if (connection == null)
                connection = db.Connections[0];

            return (db, connection);
        }

        static void Main(string[] args)
        {
            var parserResult = Parser.Default
                .ParseArguments<Options, CreateModelsOptions, CreateSqlOptions, CreateDatabaseOptions, ListOptions>(args);

            parserResult
                .WithParsed<Options>(options =>
                {
                    if (options.Verbose)
                    {
                        Verbose = true;
                        Console.WriteLine($"Verbose output enabled.");
                    }

                    if (options.ConfigPath != null)
                    {
                        ConfigPath = Path.GetFullPath(options.ConfigPath);
                        Console.WriteLine($"Reading config from {ConfigPath}");
                    }
                })
                .WithParsed<ListOptions>(options =>
                {
                    if (ReadConfig() == false)
                        return;

                    Console.WriteLine($"Databases in config:");
                    foreach (var db in ConfigFile.Databases)
                    {
                        Console.WriteLine($"{db.Name}");
                        Console.WriteLine("Connections:");
                        foreach (var connection in db.Connections)
                        {
                            Console.WriteLine($"{connection.ParsedType} ({connection.ConnectionString})");
                        }

                        var reader = new ModelReader(Console.WriteLine);
                        reader.Read(ConfigFile, ConfigBasePath);
                        Console.WriteLine();
                    }
                })
                .WithParsed<CreateModelsOptions>(options =>
                {
                    if (ReadConfig() == false)
                        return;

                    var result = GetConnection(options.SchemaName, options.ConnectionType);
                    if (result.HasFailed)
                    {
                        Console.WriteLine(result.Failure);
                        return;
                    }

                    var (db, connection) = result.Value;
                    var creator = new ModelGenerator(Console.WriteLine, new ModelGeneratorOptions
                    {
                        OverwriteExistingModels = true,
                        ReadSourceModels = !options.SkipSource,
                        CapitalizeNames = !db.CapitalizeNames.HasValue || db.CapitalizeNames == true
                    });

                    creator.Create(db, connection, ConfigBasePath, options.DatabaseName ?? connection.DatabaseName ?? options.SchemaName);
                })
                .WithParsed<CreateSqlOptions>(options =>
                {
                    if (ReadConfig() == false)
                        return;

                    var result = GetConnection(options.SchemaName, options.ConnectionType);
                    if (result.HasFailed)
                    {
                        Console.WriteLine(result.Failure);
                        return;
                    }

                    var (db, connection) = result.Value;
                    var generator = new SqlGenerator(Console.WriteLine, new SqlGeneratorOptions
                    {
                    });

                    generator.Create(db, connection, ConfigBasePath, options.OutputFile);
                })
                .WithParsed<CreateDatabaseOptions>(options =>
                {
                    if (ReadConfig() == false)
                        return;

                    var result = GetConnection(options.SchemaName, options.ConnectionType);
                    if (result.HasFailed)
                    {
                        Console.WriteLine(result.Failure);
                        return;
                    }

                    var (db, connection) = result.Value;
                    var generator = new DatabaseCreator(Console.WriteLine, new DatabaseCreatorOptions
                    {
                    });

                    generator.Create(db, connection, ConfigBasePath, options.DatabaseName ?? connection.DatabaseName ?? options.SchemaName);
                })
                .WithNotParsed(options =>
                {
                    Console.WriteLine($"Usage: datalinq [command] -n name");
                    //Console.WriteLine(HelpText.AutoBuild(parserResult, _ => _, _ => _));
                });
        }
    }
}