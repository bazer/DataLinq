using CommandLine.Text;
using CommandLine;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools;
using DataLinq.Tools.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.AccessControl;

namespace DataLinq.CLI
{
    static class Program
    {
        [Verb("create-database", HelpText = "Create selected database")]
        public class CreateDatabaseOptions : Options
        {
            [Option('n', "name", HelpText = "Database name", Required = true)]
            public string Name { get; set; }
        }

        [Verb("create-models", HelpText = "Create models for selected database")]
        public class CreateDatabaseModelsOptions : Options
        {
            [Option('n', "name", HelpText = "Database name", Required = true)]
            public string Name { get; set; }

            [Option('t', "type", HelpText = "Which database connection type to read from", Required = false)]
            public string ConnectionType { get; set; }

        }

        [Verb("delete", HelpText = "Delete selected database")]
        public class DeleteDatabaseOptions : Options
        {
            [Option('n', "name", HelpText = "Database name", Required = true)]
            public string Name { get; set; }
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

        static void Main(string[] args)
        {
            //var configPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json";

           
            //var DbName = args[0];
            //var Namespace = args[1];
            //var WritePath = Path.GetFullPath(args[2]);

            //new ModelCreator(Console.WriteLine).Create(config);

            var parserResult = Parser.Default
                .ParseArguments<Options, CreateDatabaseModelsOptions, CreateDatabaseOptions, DeleteDatabaseOptions, ListOptions>(args);

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


                    //if (!File.Exists(ConfigPath))
                    //{
                    //    Console.WriteLine($"Couldn't find config file 'datalinq.json'. Tried searching path:");
                    //    Console.WriteLine(ConfigPath);
                        
                    //    return;
                    //}

                    //ConfigFile = ConfigReader.Read(ConfigPath);
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
                .WithParsed<CreateDatabaseModelsOptions>(options =>
                {
                    if (ReadConfig() == false)
                        return;

                    var db = ConfigFile.Databases.SingleOrDefault(x => x.Name.ToLower() == options.Name.ToLower());
                    if (db == null)
                    {
                        Console.WriteLine($"Couldn't find database with name '{options.Name}'");
                        return;
                    }

                    if (db.Connections.Count == 0)
                    {
                        Console.WriteLine($"Database '{options.Name}' has no connections to read from");
                        return;
                    }

                    if (db.Connections.Count > 1 && options.ConnectionType == null)
                    {
                        Console.WriteLine($"Database '{options.Name}' has more than one connection to read from, you need to select which one");
                        return;
                    }

                    DatabaseConnectionConfig connection = null;
                    if (options.ConnectionType != null)
                    {
                        connection = db.Connections.SingleOrDefault(x => x.Type.ToLower() == options.ConnectionType.ToLower());

                        if (connection == null)
                        {
                            Console.WriteLine($"Couldn't find connection with type '{options.ConnectionType}' in configuration file.");
                            return;
                        }
                    }

                    if (connection == null)
                        connection = db.Connections[0];

                    var creator = new ModelCreator(Console.WriteLine, new ModelCreatorOptions
                    {
                        OverwriteExistingModels = true,
                        ReadSourceModels = true
                    });

                    creator.Create(db, connection, ConfigBasePath);
                })
                //.WithParsed<DeleteDatabaseOptions>(options =>
                //{

                //})
                .WithNotParsed(options =>
                {
                    Console.WriteLine($"Usage: datalinq [command] -n name");
                    //Console.WriteLine(HelpText.AutoBuild(parserResult, _ => _, _ => _));
                });
        }
    }
}


//var configPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json";

//var config = ConfigReader.Read(configPath);
////var DbName = args[0];
////var Namespace = args[1];
////var WritePath = Path.GetFullPath(args[2]);

//new ModelCreator(Console.WriteLine).Create(config);

//MySqlDatabase<information_schema> GetDatabase()
//{
//    var builder = new ConfigurationBuilder()
//        .SetBasePath(Directory.GetCurrentDirectory())
//        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

//    var configuration = builder.Build();

//    var connectionString = configuration.GetConnectionString("information_schema");
//    return new MySqlDatabase<information_schema>(connectionString);
//}

