using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommandLine;
using DataLinq.Config;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tools;

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

    [Verb("validate", HelpText = "Validate configured model metadata against a live database.")]
    public class ValidateOptions : CreateOptions
    {
        [Option("output", Required = false, Default = "text", HelpText = "Output format: text or json.")]
        public string Output { get; set; } = "text";
    }

    [Verb("diff", HelpText = "Generate a conservative SQL script for configured model drift.")]
    public class DiffOptions : CreateOptions
    {
        [Option('o', "output", HelpText = "Path to output file. If omitted, the script is written to stdout.", Required = false)]
        public string OutputFile { get; set; } = "";
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

    static public bool ReadConfig(Action<string>? log = null)
    {
        var configLog = log ?? Console.WriteLine;
        var config = DataLinqConfig.FindAndReadConfigs(ConfigPath, configLog);

        if (config.HasFailed)
        {
            Console.WriteLine(config.Failure);
            return false;
        }

        configLog("");
        ConfigFile = config.Value;

        return true;
    }

    static void Main(string[] args)
    {
        MySQLProvider.RegisterProvider();
        MariaDBProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();

        var exitCode = 0;

        var parserResult = Parser.Default
            .ParseArguments<Options, CreateModelsOptions, CreateSqlOptions, CreateDatabaseOptions, ValidateOptions, DiffOptions, ListOptions>(args);

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
                {
                    exitCode = 2;
                    return;
                }


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
                        exitCode = 2;
                        return;
                    }

                    //Console.WriteLine();
                }
            })
            .WithParsed<CreateModelsOptions>(options =>
            {
                if (ReadConfig() == false)
                {
                    exitCode = 2;
                    return;
                }

                var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
                if (result.HasFailed)
                {
                    Console.WriteLine(result.Failure);
                    exitCode = 2;
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
                    exitCode = 2;
                    return;
                }
            })
            .WithParsed<CreateSqlOptions>(options =>
            {
                if (ReadConfig() == false)
                {
                    exitCode = 2;
                    return;
                }

                var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
                if (result.HasFailed)
                {
                    Console.WriteLine(result.Failure);
                    exitCode = 2;
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
                    exitCode = 2;
                    return;
                }
            })
            .WithParsed<CreateDatabaseOptions>(options =>
            {
                if (ReadConfig() == false)
                {
                    exitCode = 2;
                    return;
                }

                var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
                if (result.HasFailed)
                {
                    Console.WriteLine(result.Failure);
                    exitCode = 2;
                    return;
                }

                var (db, connection) = result.Value;
                var generator = new DatabaseCreator(Console.WriteLine, new DatabaseCreatorOptions
                {
                });

                var createResult = generator.Create(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? options.Name);
                if (createResult.HasFailed)
                {
                    Console.WriteLine(createResult.Failure);
                    exitCode = 2;
                    return;
                }
            })
            .WithParsed<ValidateOptions>(options =>
            {
                exitCode = Validate(options);
            })
            .WithParsed<DiffOptions>(options =>
            {
                exitCode = Diff(options);
            })
            .WithNotParsed(options =>
            {
                Console.WriteLine($"Usage: datalinq [command] -n name");
                exitCode = 2;
                //Console.WriteLine(HelpText.AutoBuild(parserResult, _ => _, _ => _));
            });

        Environment.ExitCode = exitCode;
    }

    private static int Validate(ValidateOptions options)
    {
        var output = ParseValidationOutput(options.Output);
        if (output == null)
        {
            Console.WriteLine("Invalid output format. Expected 'text' or 'json'.");
            return 2;
        }

        if (!TryValidateSchema(options, output == ValidationOutput.Json && !options.Verbose, out var validation))
            return 2;

        if (output == ValidationOutput.Json)
            WriteValidationJson(validation);
        else
            WriteValidationText(validation);

        return validation.HasDifferences ? 1 : 0;
    }

    private static int Diff(DiffOptions options)
    {
        if (!TryValidateSchema(options, !options.Verbose, out var validation))
            return 2;

        var script = new SchemaDiffScriptGenerator().Generate(validation.DatabaseType, validation.Differences);

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            File.WriteAllText(options.OutputFile, script);
            if (options.Verbose)
                Console.WriteLine($"Wrote schema diff script to {options.OutputFile}");
        }
        else
        {
            Console.Write(script);
        }

        return 0;
    }

    private static bool TryValidateSchema(CreateOptions options, bool quietConfig, out SchemaValidationRunResult validationResult)
    {
        validationResult = null!;

        var configLog = quietConfig
            ? new Action<string>(_ => { })
            : Console.WriteLine;

        if (ReadConfig(configLog) == false)
            return false;

        var result = ConfigFile.GetConnection(options.Name, ConfigReader.ParseDatabaseType(options.ConnectionType));
        if (result.HasFailed)
        {
            Console.WriteLine(result.Failure);
            return false;
        }

        var (_, connection) = result.Value;
        var validator = new SchemaValidator(options.Verbose ? Console.WriteLine : _ => { });
        var validation = validator.Validate(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? options.Name);
        if (validation.HasFailed)
        {
            Console.WriteLine(validation.Failure);
            return false;
        }

        validationResult = validation.Value;
        return true;
    }

    private static ValidationOutput? ParseValidationOutput(string value)
    {
        return value?.ToLowerInvariant() switch
        {
            "text" => ValidationOutput.Text,
            "json" => ValidationOutput.Json,
            _ => null
        };
    }

    private static void WriteValidationText(SchemaValidationRunResult result)
    {
        Console.WriteLine($"Validation target: {result.DatabaseName} [{result.DatabaseType}] ({result.DataSourceName})");
        Console.WriteLine($"Model tables: {result.ModelTableCount}; database tables: {result.DatabaseTableCount}");

        if (!result.HasDifferences)
        {
            Console.WriteLine("No schema drift detected.");
            return;
        }

        Console.WriteLine($"Schema drift detected: {result.Differences.Count} difference(s).");
        foreach (var difference in result.Differences)
        {
            Console.WriteLine(
                $"{difference.Severity} {difference.Kind} {difference.Path} [{difference.Safety}]: {difference.Message}");
        }
    }

    private static void WriteValidationJson(SchemaValidationRunResult result)
    {
        var payload = new
        {
            database = result.DatabaseName,
            databaseType = result.DatabaseType.ToString(),
            dataSource = result.DataSourceName,
            modelTableCount = result.ModelTableCount,
            databaseTableCount = result.DatabaseTableCount,
            hasDifferences = result.HasDifferences,
            differences = result.Differences.Select(difference => new
            {
                kind = difference.Kind.ToString(),
                severity = difference.Severity.ToString(),
                safety = difference.Safety.ToString(),
                path = difference.Path,
                message = difference.Message
            })
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private enum ValidationOutput
    {
        Text,
        Json
    }
}
