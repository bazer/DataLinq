using System;
using System.IO;
using CommandLine;
using DataLinq.Config;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tools;
using ThrowAway;

namespace DataLinq.CLI;

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
        [Option("skip-source", HelpText = "Skip reading from source models", Required = false)]
        public bool SkipSource { get; set; }

        [Option("overwrite-types", Required = false, HelpText = "Force overwriting C# property types in existing models with types inferred from the database schema.")]
        public bool OverwriteTypes { get; set; }
    }

    public class CreateOptions : Options
    {
        [Option('d', "datasource", HelpText = "Name of the database instance on the server or file on disk, depending on the connection type", Required = false)]
        public string DataSource { get; set; }

        [Option('n', "name", HelpText = "Name in the DataLinq config file", Required = false)]
        public string Name { get; set; }

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
    static DataLinqConfig ConfigFile;
    static string ConfigBasePath => ConfigFile.BasePath;

    static public bool ReadConfig()
    {
        var config = DataLinqConfig.FindAndReadConfigs(ConfigPath, Console.WriteLine);

        if (config.HasFailed)
        {
            Console.WriteLine(config.Failure);
            return false;
        }

        Console.WriteLine();
        ConfigFile = config.Value;

        return true;
    }

    static void Main(string[] args)
    {
        MySQLProvider.RegisterProvider();
        MariaDBProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();


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
                    //Console.WriteLine($"Reading config from {ConfigPath}");
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
                        Console.WriteLine($"{connection.Type} ({connection.DataSourceName})");
                    }

                    Console.WriteLine();

                    var reader = new ModelReader(Console.WriteLine);
                    var result = reader.Read(ConfigFile, ConfigBasePath);

                    if (result.HasFailed)
                    {
                        Console.WriteLine(result.Failure);
                        return;
                    }

                    //Console.WriteLine();
                }
            })
            .WithParsed<CreateModelsOptions>(options =>
            {
                if (ReadConfig() == false)
                    return;

                var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
                if (result.HasFailed)
                {
                    Console.WriteLine(result.Failure);
                    return;
                }

                var (db, connection) = result.Value;
                var generator = new ModelGenerator(Console.WriteLine, new ModelGeneratorOptions
                {
                    OverwriteExistingModels = true,
                    ReadSourceModels = !options.SkipSource,
                    CapitalizeNames = db.CapitalizeNames,
                    Include = db.Include,
                    OverwritePropertyTypes = options.OverwriteTypes
                });

                var databaseMetadata = generator.CreateModels(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? options.Name);

                if (databaseMetadata.HasFailed)
                {
                    Console.WriteLine(databaseMetadata.Failure);
                    return;
                }
            })
            .WithParsed<CreateSqlOptions>(options =>
            {
                if (ReadConfig() == false)
                    return;

                var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
                if (result.HasFailed)
                {
                    Console.WriteLine(result.Failure);
                    return;
                }

                var (db, connection) = result.Value;
                var generator = new SqlGenerator(Console.WriteLine, new SqlGeneratorOptions
                {
                });

                var sql = generator.Create(connection, ConfigBasePath, options.OutputFile);

                if (sql.HasFailed)
                {
                    Console.WriteLine(sql.Failure);
                    return;
                }
            })
            .WithParsed<CreateDatabaseOptions>(options =>
            {
                if (ReadConfig() == false)
                    return;

                var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
                if (result.HasFailed)
                {
                    Console.WriteLine(result.Failure);
                    return;
                }

                var (db, connection) = result.Value;
                var generator = new DatabaseCreator(Console.WriteLine, new DatabaseCreatorOptions
                {
                });

                generator.Create(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? options.Name);
            })
            .WithNotParsed(options =>
            {
                Console.WriteLine($"Usage: datalinq [command] -n name");
                //Console.WriteLine(HelpText.AutoBuild(parserResult, _ => _, _ => _));
            });
    }
}