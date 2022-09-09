using CommandLine.Text;
using CommandLine;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tools;
using DataLinq.Tools.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.CLI
{
    static class Program
    {
        [Verb("create", HelpText = "Create selected database")]
        public class CreateDatabaseOptions : Options
        {
            [Option('n', "name", HelpText = "Database name", Required = true)]
            public string Name { get; set; }
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

        //static public ConfigFile ReadConfig()
        //{
        //    if (!File.Exists(ConfigPath))
        //    {
        //        Console.WriteLine($"Couldn't find config file 'datalinq.json'. Tried searching path:");
        //        Console.WriteLine(ConfigPath);
        //        return null;
        //    }

        //    return ConfigReader.Read(ConfigPath);
        //}

        static void Main(string[] args)
        {
            //var configPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json";

           
            //var DbName = args[0];
            //var Namespace = args[1];
            //var WritePath = Path.GetFullPath(args[2]);

            //new ModelCreator(Console.WriteLine).Create(config);

            var parserResult = Parser.Default
                .ParseArguments<Options, CreateDatabaseOptions, DeleteDatabaseOptions, ListOptions>(args);

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
                        ConfigPath = options.ConfigPath;
                        Console.WriteLine($"Reading config from {ConfigPath}");
                    }


                    if (!File.Exists(ConfigPath))
                    {
                        Console.WriteLine($"Couldn't find config file 'datalinq.json'. Tried searching path:");
                        Console.WriteLine(ConfigPath);
                        return;
                    }

                    ConfigFile = ConfigReader.Read(ConfigPath);
                })
                .WithParsed<ListOptions>(options =>
                {
                    Console.WriteLine($"Databases in config:");
                    foreach (var db in ConfigFile.Databases)
                    {
                        Console.WriteLine($"{db.Name} ({db.Type})");
                    }
                })
                //.WithParsed<CreateDatabaseOptions>(options =>
                //{
              
                //})
                //.WithParsed<DeleteDatabaseOptions>(options =>
                //{
               
                //})
                .WithNotParsed(options =>
                {
                    Console.WriteLine($"Usage: datalinq [command] -n name");
                    Console.WriteLine(HelpText.AutoBuild(parserResult, _ => _, _ => _));
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

