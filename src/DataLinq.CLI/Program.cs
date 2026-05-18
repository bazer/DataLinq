using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tools;
using DataLinq.Validation;

namespace DataLinq.CLI;

internal static class Program
{
    private static readonly string DefaultConfigPath =
        $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json";

    private static string ConfigPath = DefaultConfigPath;
    private static DataLinqConfig ConfigFile = null!;
    private static string ConfigBasePath => ConfigFile.BasePath;

    public static async Task<int> Main(string[] args)
    {
        var exitCode = await InvokeAsync(args);
        Environment.ExitCode = exitCode;
        return exitCode;
    }

    internal static async Task<int> InvokeAsync(string[] args)
    {
        RegisterProviders();
        ResetState();

        var rootCommand = CreateRootCommand();
        var parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
                ConsoleDiagnosticWriter.WriteError("InvalidCommand", error.Message);

            return 2;
        }

        Environment.ExitCode = 0;
        var exitCode = await parseResult.InvokeAsync();
        return Environment.ExitCode != 0 ? Environment.ExitCode : exitCode;
    }

    internal static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("DataLinq command-line tools.");

        var generateCommand = new Command("generate", "Generate DataLinq artifacts.");
        generateCommand.Subcommands.Add(CreateGenerateModelsCommand());
        generateCommand.Subcommands.Add(CreateGenerateSqlCommand());

        var databaseCommand = new Command("database", "Manage configured databases.");
        databaseCommand.Subcommands.Add(CreateDatabaseCreateCommand());

        var configCommand = new Command("config", "Inspect and manage DataLinq configuration.");
        configCommand.Subcommands.Add(CreateConfigListCommand());
        configCommand.Subcommands.Add(CreateConfigSchemaCommand());

        rootCommand.Subcommands.Add(generateCommand);
        rootCommand.Subcommands.Add(databaseCommand);
        rootCommand.Subcommands.Add(CreateValidateCommand());
        rootCommand.Subcommands.Add(CreateDiffCommand());
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(CreateDeprecatedCreateModelsCommand());

        return rootCommand;
    }

    private static Command CreateGenerateModelsCommand()
    {
        var command = new Command("models", "Generate C# models for the selected database.");
        var options = AddModelGenerationOptions(command);

        command.SetAction(parseResult =>
        {
            var createOptions = ReadCreateModelsOptions(parseResult, options);
            Environment.ExitCode = ExecuteCreateModels(createOptions);
        });

        return command;
    }

    private static Command CreateDeprecatedCreateModelsCommand()
    {
        var command = new Command("create-models", "Deprecated. Use 'generate models'.");
        var options = AddModelGenerationOptions(command);

        command.SetAction(parseResult =>
        {
            ConsoleDiagnosticWriter.WriteWarning("DeprecatedCommand", "create-models is deprecated. Use generate models.");
            var createOptions = ReadCreateModelsOptions(parseResult, options);
            Environment.ExitCode = ExecuteCreateModels(createOptions);
        });

        return command;
    }

    private static Command CreateGenerateSqlCommand()
    {
        var command = new Command("sql", "Generate SQL for the selected database.");
        var targetOptions = AddTargetOptions(command);
        var outputOption = OutputFileOption(required: true);
        command.Options.Add(outputOption);

        command.SetAction(parseResult =>
        {
            var options = ReadCreateSqlOptions(parseResult, targetOptions, outputOption);
            Environment.ExitCode = ExecuteCreateSql(options);
        });

        return command;
    }

    private static Command CreateDatabaseCreateCommand()
    {
        var command = new Command("create", "Create the selected database.");
        var targetOptions = AddTargetOptions(command);

        command.SetAction(parseResult =>
        {
            var options = ReadCreateDatabaseOptions(parseResult, targetOptions);
            Environment.ExitCode = ExecuteCreateDatabase(options);
        });

        return command;
    }

    private static Command CreateValidateCommand()
    {
        var command = new Command("validate", "Validate configured model metadata against a live database.");
        var targetOptions = AddBatchTargetOptions(command);
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: text or json.",
            DefaultValueFactory = _ => "text"
        };
        command.Options.Add(formatOption);

        command.SetAction(parseResult =>
        {
            var options = ReadValidateOptions(parseResult, targetOptions, formatOption);
            Environment.ExitCode = Validate(options);
        });

        return command;
    }

    private static Command CreateDiffCommand()
    {
        var command = new Command("diff", "Generate a conservative SQL script for configured model drift.");
        var targetOptions = AddTargetOptions(command);
        var outputOption = OutputFileOption(required: false);
        command.Options.Add(outputOption);

        command.SetAction(parseResult =>
        {
            var options = ReadDiffOptions(parseResult, targetOptions, outputOption);
            Environment.ExitCode = Diff(options);
        });

        return command;
    }

    private static Command CreateConfigListCommand()
    {
        var command = new Command("list", "List databases and connections in the selected config.");
        var options = AddConfigListOptions(command);

        command.SetAction(parseResult =>
        {
            var listOptions = ReadListOptions(parseResult, options);
            Environment.ExitCode = ExecuteList(listOptions);
        });

        return command;
    }

    private static Command CreateConfigSchemaCommand()
    {
        var command = new Command("schema", "Print or write the DataLinq JSON Schema for config files.");
        var commonOptions = AddCommonOptions(command);
        var outputOption = OutputFileOption(required: false);
        command.Options.Add(outputOption);

        command.SetAction(parseResult =>
        {
            var options = ReadConfigSchemaOptions(parseResult, commonOptions, outputOption);
            Environment.ExitCode = ExecuteConfigSchema(options);
        });

        return command;
    }

    private static ModelGenerationOptionSet AddModelGenerationOptions(Command command)
    {
        var targetOptions = AddBatchTargetOptions(command);
        var freshOption = new Option<bool>("--fresh")
        {
            Description = "Ignore existing model source and generate from database metadata plus datalinq.json only."
        };
        var overwriteTypesOption = new Option<bool>("--overwrite-types")
        {
            Description = "Force overwriting C# property types in existing models with types inferred from the database schema."
        };
        var stampGeneratedHeaderOption = new Option<bool>("--stamp-generated-header")
        {
            Description = "Include the DataLinq CLI version and UTC generation timestamp in generated model file headers."
        };

        command.Options.Add(freshOption);
        command.Options.Add(overwriteTypesOption);
        command.Options.Add(stampGeneratedHeaderOption);

        return new ModelGenerationOptionSet(
            targetOptions,
            freshOption,
            overwriteTypesOption,
            stampGeneratedHeaderOption);
    }

    private static BatchTargetOptionSet AddBatchTargetOptions(Command command)
    {
        var targetOptions = AddTargetOptions(command);
        var allOption = new Option<bool>("--all")
        {
            Description = "Run against all databases and connections in the selected config."
        };
        var recursiveOption = new Option<bool>("--recursive")
        {
            Description = "Discover datalinq.json files recursively and run against all matching targets."
        };

        command.Options.Add(allOption);
        command.Options.Add(recursiveOption);

        return new BatchTargetOptionSet(targetOptions, allOption, recursiveOption);
    }

    private static TargetOptionSet AddTargetOptions(Command command)
    {
        var commonOptions = AddCommonOptions(command);
        var dataSourceOption = new Option<string?>("--data-source")
        {
            Description = "Name of the database instance on the server or file on disk, depending on the connection type."
        };
        var databaseOption = new Option<string?>("--database")
        {
            Description = "Database name in datalinq.json."
        };
        databaseOption.Aliases.Add("-n");
        var providerOption = new Option<string?>("--provider")
        {
            Description = "Database provider to use for the selected config entry."
        };
        providerOption.Aliases.Add("-p");

        command.Options.Add(dataSourceOption);
        command.Options.Add(databaseOption);
        command.Options.Add(providerOption);

        return new TargetOptionSet(commonOptions, dataSourceOption, databaseOption, providerOption);
    }

    private static CommonOptionSet AddCommonOptions(Command command)
    {
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Print verbose messages."
        };
        verboseOption.Aliases.Add("-v");

        var configOption = new Option<string?>("--config")
        {
            Description = "Path to datalinq.json or its containing directory."
        };
        configOption.Aliases.Add("-c");

        command.Options.Add(verboseOption);
        command.Options.Add(configOption);

        return new CommonOptionSet(verboseOption, configOption);
    }

    private static ConfigListOptionSet AddConfigListOptions(Command command)
    {
        var commonOptions = AddCommonOptions(command);
        var recursiveOption = new Option<bool>("--recursive")
        {
            Description = "Discover datalinq.json files recursively and list each readable config."
        };
        command.Options.Add(recursiveOption);

        return new ConfigListOptionSet(commonOptions, recursiveOption);
    }

    private static Option<string?> OutputFileOption(bool required)
    {
        var option = new Option<string?>("--output")
        {
            Description = required
                ? "Path to output file."
                : "Path to output file. If omitted, output is written to stdout.",
            Required = required
        };
        option.Aliases.Add("-o");
        return option;
    }

    private static CreateModelsOptions ReadCreateModelsOptions(ParseResult parseResult, ModelGenerationOptionSet options)
    {
        var createOptions = ReadTargetOptions<CreateModelsOptions>(parseResult, options.TargetOptions.TargetOptions);
        createOptions.All = parseResult.GetValue(options.TargetOptions.AllOption);
        createOptions.Recursive = parseResult.GetValue(options.TargetOptions.RecursiveOption);
        createOptions.Fresh = parseResult.GetValue(options.FreshOption);
        createOptions.OverwriteTypes = parseResult.GetValue(options.OverwriteTypesOption);
        createOptions.StampGeneratedHeader = parseResult.GetValue(options.StampGeneratedHeaderOption);
        return createOptions;
    }

    private static CreateSqlOptions ReadCreateSqlOptions(
        ParseResult parseResult,
        TargetOptionSet targetOptions,
        Option<string?> outputOption)
    {
        var createOptions = ReadTargetOptions<CreateSqlOptions>(parseResult, targetOptions);
        createOptions.OutputFile = parseResult.GetValue(outputOption) ?? "";
        return createOptions;
    }

    private static CreateDatabaseOptions ReadCreateDatabaseOptions(ParseResult parseResult, TargetOptionSet targetOptions) =>
        ReadTargetOptions<CreateDatabaseOptions>(parseResult, targetOptions);

    private static ValidateOptions ReadValidateOptions(
        ParseResult parseResult,
        BatchTargetOptionSet targetOptions,
        Option<string> formatOption)
    {
        var validateOptions = ReadTargetOptions<ValidateOptions>(parseResult, targetOptions.TargetOptions);
        validateOptions.All = parseResult.GetValue(targetOptions.AllOption);
        validateOptions.Recursive = parseResult.GetValue(targetOptions.RecursiveOption);
        validateOptions.Format = parseResult.GetValue(formatOption) ?? "text";
        return validateOptions;
    }

    private static DiffOptions ReadDiffOptions(
        ParseResult parseResult,
        TargetOptionSet targetOptions,
        Option<string?> outputOption)
    {
        var diffOptions = ReadTargetOptions<DiffOptions>(parseResult, targetOptions);
        diffOptions.OutputFile = parseResult.GetValue(outputOption) ?? "";
        return diffOptions;
    }

    private static ListOptions ReadListOptions(ParseResult parseResult, ConfigListOptionSet options)
    {
        var listOptions = new ListOptions();
        ApplyCommonOptions(listOptions, parseResult, options.CommonOptions);
        listOptions.Recursive = parseResult.GetValue(options.RecursiveOption);
        return listOptions;
    }

    private static ConfigSchemaOptions ReadConfigSchemaOptions(
        ParseResult parseResult,
        CommonOptionSet commonOptions,
        Option<string?> outputOption)
    {
        var schemaOptions = new ConfigSchemaOptions
        {
            OutputFile = parseResult.GetValue(outputOption) ?? ""
        };
        ApplyCommonOptions(schemaOptions, parseResult, commonOptions);
        return schemaOptions;
    }

    private static T ReadTargetOptions<T>(ParseResult parseResult, TargetOptionSet options)
        where T : CreateOptions, new()
    {
        var createOptions = new T
        {
            DataSource = parseResult.GetValue(options.DataSourceOption),
            Database = parseResult.GetValue(options.DatabaseOption),
            Provider = parseResult.GetValue(options.ProviderOption)
        };
        ApplyCommonOptions(createOptions, parseResult, options.CommonOptions);
        return createOptions;
    }

    private static void ApplyCommonOptions(Options options, ParseResult parseResult, CommonOptionSet optionSet)
    {
        options.Verbose = parseResult.GetValue(optionSet.VerboseOption);
        options.ConfigPath = parseResult.GetValue(optionSet.ConfigOption);

        if (options.Verbose)
            Console.WriteLine("Verbose output enabled.");

        if (options.ConfigPath != null)
            ConfigPath = Path.GetFullPath(options.ConfigPath);
    }

    private static int ExecuteList(ListOptions options)
    {
        if (options.Recursive)
            return ExecuteRecursiveList(options);

        if (ReadConfig() == false)
            return 2;

        WriteConfigList(ConfigFile, CliConfigDiscovery.ResolveConfigFilePath(ConfigPath));
        return 0;
    }

    private static int ExecuteRecursiveList(ListOptions options)
    {
        var configPaths = CliConfigDiscovery.DiscoverConfigFiles(ConfigPath);
        if (configPaths.Count == 0)
        {
            ConsoleDiagnosticWriter.WriteError(
                "ConfigNotFound",
                $"No datalinq.json files were found under '{CliConfigDiscovery.ResolveDiscoveryRoot(ConfigPath)}'.");
            return 2;
        }

        var hadFailure = false;
        var listedCount = 0;
        foreach (var configPath in configPaths)
        {
            var configLog = options.Verbose
                ? ConsoleDiagnosticWriter.WriteLogLine
                : new Action<string>(_ => { });
            if (!CliConfigLoader.TryRead(configPath, configLog, out var config, out var failure))
            {
                hadFailure = true;
                ConsoleDiagnosticWriter.WriteError("ConfigReadFailed", $"Failed to read config '{configPath}'.");
                ConsoleDiagnosticWriter.WriteFailure(failure);
                Console.WriteLine();
                continue;
            }

            WriteConfigList(config, configPath);
            listedCount++;
        }

        Console.WriteLine($"Configs listed: {listedCount}; failures: {(hadFailure ? "yes" : "no")}");
        return hadFailure ? 2 : 0;
    }

    private static void WriteConfigList(DataLinqConfig config, string configPath)
    {
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine("Databases:");
        foreach (var db in config.Databases)
        {
            Console.WriteLine(db.Name);
            Console.WriteLine("Connections:");
            foreach (var connection in db.Connections)
                Console.WriteLine($"{connection.Type} ({connection.DataSourceName})");

            Console.WriteLine();
        }
    }

    private static int ExecuteConfigSchema(ConfigSchemaOptions options)
    {
        var schemaJson = CliConfigSchema.ReadSchemaJson();
        if (string.IsNullOrWhiteSpace(options.OutputFile))
        {
            Console.Write(schemaJson);
            return 0;
        }

        var outputPath = Path.GetFullPath(options.OutputFile);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, schemaJson);
        if (options.Verbose)
            Console.WriteLine($"Wrote DataLinq config schema to {outputPath}");

        return 0;
    }

    private static int ExecuteCreateModels(CreateModelsOptions options)
    {
        if (options.All || options.Recursive)
            return ExecuteCreateModelsBatch(options);

        if (ReadConfig() == false)
            return 2;

        var result = ConfigFile.GetConnection(options.Database, ConfigReader.ParseDatabaseType(options.Provider));
        if (result.HasFailed)
        {
            ConsoleDiagnosticWriter.WriteFailure(result.Failure);
            return 2;
        }

        var (db, connection) = result.Value;
        var generator = new ModelGenerator(ConsoleDiagnosticWriter.WriteLogLine, new ModelGeneratorOptions
        {
            OverwriteExistingModels = true,
            ReadSourceModels = !options.Fresh,
            CapitalizeNames = db.CapitalizeNames,
            Include = db.Include,
            OverwritePropertyTypes = options.OverwriteTypes,
            GeneratedFileStamp = options.StampGeneratedHeader
                ? new GeneratedFileStamp(GetCliVersion(), DateTimeOffset.UtcNow)
                : null
        });

        var databaseMetadata = generator.CreateModels(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? db.Name);

        if (databaseMetadata.HasFailed)
        {
            ConsoleDiagnosticWriter.WriteFailure(databaseMetadata.Failure);
            return 2;
        }

        return 0;
    }

    private static int ExecuteCreateModelsBatch(CreateModelsOptions options)
    {
        if (!TryValidateBatchOptions(options, options.All, options.Recursive))
            return 2;

        var expansion = CliTargetResolver.Expand(
            ConfigPath,
            new CliTargetFilter(options.Database, options.Provider),
            options.Recursive,
            options.Verbose ? ConsoleDiagnosticWriter.WriteLogLine : _ => { });

        if (expansion.Targets.Count == 0 && expansion.Failures.Count == 0)
        {
            ConsoleDiagnosticWriter.WriteError("TargetNotFound", "No model generation targets matched the selected config filters.");
            return 2;
        }

        var hadFailure = false;
        var writePlans = new List<GeneratedModelWritePlan>();
        GeneratedFileStamp? generatedFileStamp = options.StampGeneratedHeader
            ? new GeneratedFileStamp(GetCliVersion(), DateTimeOffset.UtcNow)
            : null;

        foreach (var failure in expansion.Failures)
        {
            hadFailure = true;
            ConsoleDiagnosticWriter.WriteError("TargetFailed", $"Failed to prepare targets from '{failure.ConfigPath}'.");
            ConsoleDiagnosticWriter.WriteIssues(CreateIssues(failure.Failure));
        }

        foreach (var target in expansion.Targets)
        {
            Console.WriteLine();
            Console.WriteLine($"Target: {FormatTargetIdentity(target.Identity)}");

            var generator = CreateModelGenerator(target.Database, options, generatedFileStamp);
            var writePlan = generator.CreateModelWritePlan(
                target.Connection,
                target.Config.BasePath,
                target.Connection.DataSourceName);

            if (writePlan.HasFailed)
            {
                hadFailure = true;
                ConsoleDiagnosticWriter.WriteFailure(writePlan.Failure);
                continue;
            }

            writePlans.Add(writePlan.Value);
        }

        if (TryFindDuplicateGeneratedPath(writePlans, out var duplicatePath))
        {
            hadFailure = true;
            ConsoleDiagnosticWriter.WriteError(
                "InvalidOutput",
                $"Batch model generation produced duplicate target path '{duplicatePath}'. No files were written.");
        }

        if (hadFailure)
        {
            Console.WriteLine();
            Console.WriteLine($"Model generation targets: {expansion.Targets.Count}; failures: yes; files written: no");
            return 2;
        }

        foreach (var writePlan in writePlans)
        {
            var writeResult = writePlan.Write(ConsoleDiagnosticWriter.WriteLogLine);
            if (writeResult.HasFailed)
            {
                ConsoleDiagnosticWriter.WriteFailure(writeResult.Failure);
                return 2;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Model generation targets: {writePlans.Count}; failures: no; files written: yes");
        return 0;
    }

    private static int ExecuteCreateSql(CreateSqlOptions options)
    {
        if (ReadConfig() == false)
            return 2;

        var result = ConfigFile.GetConnection(options.Database, ConfigReader.ParseDatabaseType(options.Provider));
        if (result.HasFailed)
        {
            ConsoleDiagnosticWriter.WriteFailure(result.Failure);
            return 2;
        }

        var (_, connection) = result.Value;
        var generator = new SqlGenerator(ConsoleDiagnosticWriter.WriteLogLine, new SqlGeneratorOptions
        {
        });

        var sql = generator.Create(connection, ConfigBasePath, options.OutputFile);

        if (sql.HasFailed)
        {
            ConsoleDiagnosticWriter.WriteFailure(sql.Failure);
            return 2;
        }

        return 0;
    }

    private static ModelGenerator CreateModelGenerator(
        DataLinqDatabaseConfig database,
        CreateModelsOptions options,
        GeneratedFileStamp? generatedFileStamp) =>
        new(ConsoleDiagnosticWriter.WriteLogLine, new ModelGeneratorOptions
        {
            OverwriteExistingModels = true,
            ReadSourceModels = !options.Fresh,
            CapitalizeNames = database.CapitalizeNames,
            Include = database.Include,
            OverwritePropertyTypes = options.OverwriteTypes,
            GeneratedFileStamp = generatedFileStamp
        });

    private static bool TryFindDuplicateGeneratedPath(
        IEnumerable<GeneratedModelWritePlan> writePlans,
        out string duplicatePath)
    {
        duplicatePath = "";
        var duplicate = writePlans
            .SelectMany(writePlan => writePlan.Files.Select(file => file.path))
            .GroupBy(path => Path.GetFullPath(path), GetPathComparer())
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate == null)
            return false;

        duplicatePath = duplicate.Key;
        return true;
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static int ExecuteCreateDatabase(CreateDatabaseOptions options)
    {
        if (ReadConfig() == false)
            return 2;

        var result = ConfigFile.GetConnection(options.Database, ConfigReader.ParseDatabaseType(options.Provider));
        if (result.HasFailed)
        {
            ConsoleDiagnosticWriter.WriteFailure(result.Failure);
            return 2;
        }

        var (db, connection) = result.Value;
        var generator = new DatabaseCreator(ConsoleDiagnosticWriter.WriteLogLine, new DatabaseCreatorOptions
        {
        });

        var createResult = generator.Create(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? db.Name);
        if (createResult.HasFailed)
        {
            ConsoleDiagnosticWriter.WriteFailure(createResult.Failure);
            return 2;
        }

        return 0;
    }

    private static int Validate(ValidateOptions options)
    {
        var output = ParseValidationOutput(options.Format);
        if (output == null)
        {
            ConsoleDiagnosticWriter.WriteError("InvalidArgument", "Invalid output format. Expected 'text' or 'json'.");
            return 2;
        }

        if (options.All || options.Recursive)
            return ValidateBatch(options, output.Value);

        if (!TryValidateSchema(options, output == ValidationOutput.Json && !options.Verbose, out var validation, out var issues))
        {
            if (output == ValidationOutput.Json)
                WriteValidationFailureJson(issues);
            else
                ConsoleDiagnosticWriter.WriteIssues(issues);

            return 2;
        }

        if (output == ValidationOutput.Json)
            WriteValidationJson(validation);
        else
            WriteValidationText(validation);

        return validation.HasDifferences ? 1 : 0;
    }

    private static int ValidateBatch(ValidateOptions options, ValidationOutput output)
    {
        if (!TryValidateBatchOptions(options, options.All, options.Recursive))
            return 2;

        var expansion = CliTargetResolver.Expand(
            ConfigPath,
            new CliTargetFilter(options.Database, options.Provider),
            options.Recursive,
            output == ValidationOutput.Json || !options.Verbose
                ? _ => { }
                : ConsoleDiagnosticWriter.WriteLogLine);

        if (expansion.Targets.Count == 0 && expansion.Failures.Count == 0)
        {
            ConsoleDiagnosticWriter.WriteError("TargetNotFound", "No validation targets matched the selected config filters.");
            return 2;
        }

        return output == ValidationOutput.Json
            ? ValidateBatchJson(expansion, options)
            : ValidateBatchText(expansion, options);
    }

    private static int ValidateBatchText(CliTargetExpansion expansion, ValidateOptions options)
    {
        var hadFailure = false;
        var hasDifferences = false;

        foreach (var failure in expansion.Failures)
        {
            hadFailure = true;
            ConsoleDiagnosticWriter.WriteError("TargetFailed", $"Failed to prepare targets from '{failure.ConfigPath}'.");
            ConsoleDiagnosticWriter.WriteIssues(CreateIssues(failure.Failure));
        }

        foreach (var target in expansion.Targets)
        {
            Console.WriteLine();
            Console.WriteLine($"Target: {FormatTargetIdentity(target.Identity)}");

            if (!TryValidateTarget(target, options.Verbose, out var validation, out var issues))
            {
                hadFailure = true;
                ConsoleDiagnosticWriter.WriteIssues(issues);
                continue;
            }

            WriteValidationText(validation);
            hasDifferences |= validation.HasDifferences;
        }

        Console.WriteLine();
        Console.WriteLine($"Validation targets: {expansion.Targets.Count}; failures: {(hadFailure ? "yes" : "no")}; drift: {(hasDifferences ? "yes" : "no")}");

        if (hadFailure)
            return 2;

        return hasDifferences ? 1 : 0;
    }

    private static int ValidateBatchJson(CliTargetExpansion expansion, ValidateOptions options)
    {
        var failures = expansion.Failures
            .Select(failure => new ValidationBatchFailure(null, CreateIssues(failure.Failure)))
            .ToList();
        var results = new List<SchemaValidationRunResult>();

        foreach (var target in expansion.Targets)
        {
            if (!TryValidateTarget(target, options.Verbose, out var validation, out var issues))
            {
                failures.Add(new ValidationBatchFailure(target.Identity, issues));
                continue;
            }

            results.Add(validation);
        }

        WriteValidationBatchJson(results, failures);

        if (failures.Count > 0)
            return 2;

        return results.Any(result => result.HasDifferences) ? 1 : 0;
    }

    private static int Diff(DiffOptions options)
    {
        if (!TryValidateSchema(options, !options.Verbose, out var validation, out var issues))
        {
            ConsoleDiagnosticWriter.WriteIssues(issues);
            return 2;
        }

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

    private static bool TryValidateSchema(
        CreateOptions options,
        bool quietConfig,
        out SchemaValidationRunResult validationResult,
        out IReadOnlyList<DataLinqDiagnosticIssue> issues)
    {
        validationResult = null!;
        issues = [];

        var configLog = quietConfig
            ? new Action<string>(_ => { })
            : ConsoleDiagnosticWriter.WriteLogLine;

        if (TryReadConfig(configLog, out var configFailure) == false)
        {
            if (configFailure != null)
                issues = CreateIssues(configFailure);

            return false;
        }

        var result = ConfigFile.GetConnection(options.Database, ConfigReader.ParseDatabaseType(options.Provider));
        if (result.HasFailed)
        {
            issues = CreateIssues(result.Failure);
            return false;
        }

        var (_, connection) = result.Value;
        var validator = new SchemaValidator(options.Verbose ? ConsoleDiagnosticWriter.WriteLogLine : _ => { });
        var validation = validator.Validate(connection, ConfigBasePath, options.DataSource ?? connection.DataSourceName ?? options.Database);
        if (validation.HasFailed)
        {
            issues = CreateIssues(validation.Failure);
            return false;
        }

        validationResult = validation.Value;
        return true;
    }

    private static bool TryValidateTarget(
        CliConfigTarget target,
        bool verbose,
        out SchemaValidationRunResult validationResult,
        out IReadOnlyList<DataLinqDiagnosticIssue> issues)
    {
        validationResult = null!;
        issues = [];

        var validator = new SchemaValidator(verbose ? ConsoleDiagnosticWriter.WriteLogLine : _ => { });
        var validation = validator.Validate(
            target.Connection,
            target.Config.BasePath,
            target.Connection.DataSourceName);
        if (validation.HasFailed)
        {
            issues = CreateIssues(validation.Failure);
            return false;
        }

        validationResult = validation.Value;
        return true;
    }

    private static bool TryValidateBatchOptions(
        CreateOptions options,
        bool all,
        bool recursive)
    {
        if (all && recursive)
        {
            ConsoleDiagnosticWriter.WriteError("InvalidArgument", "Use either --all or --recursive, not both.");
            return false;
        }

        if ((all || recursive) && !string.IsNullOrWhiteSpace(options.DataSource))
        {
            ConsoleDiagnosticWriter.WriteError("InvalidArgument", "--data-source cannot be used with --all or --recursive.");
            return false;
        }

        return true;
    }

    private static bool ReadConfig(Action<string>? log = null)
    {
        if (TryReadConfig(log, out var failure))
            return true;

        ConsoleDiagnosticWriter.WriteFailure(failure);
        return false;
    }

    private static bool TryReadConfig(Action<string>? log, out object? failure)
    {
        var configLog = log ?? ConsoleDiagnosticWriter.WriteLogLine;
        if (!CliConfigLoader.TryRead(ConfigPath, configLog, out var config, out var readFailure))
        {
            failure = readFailure;
            return false;
        }

        configLog("");
        ConfigFile = config;
        failure = null;

        return true;
    }

    private static ValidationOutput? ParseValidationOutput(string value)
    {
        return value.ToLowerInvariant() switch
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

        Console.WriteLine($"Schema drift detected: {FormatDifferenceCount(result.Differences.Count)} ({FormatDifferenceSummary(result.Differences)}).");

        foreach (var group in result.Differences
            .OrderByDescending(difference => difference.Severity)
            .ThenBy(difference => difference.Safety)
            .ThenBy(difference => difference.Kind)
            .ThenBy(difference => difference.Path, StringComparer.Ordinal)
            .GroupBy(difference => difference.Severity))
        {
            Console.WriteLine();
            Console.WriteLine($"{FormatSeverityHeading(group.Key)}:");

            foreach (var difference in group)
            {
                Console.WriteLine($"  - {difference.Kind} [{difference.Safety}] {difference.Path}");
                Console.WriteLine($"    {difference.Message}");
            }
        }
    }

    private static string FormatSeverityHeading(SchemaDifferenceSeverity severity) =>
        severity switch
        {
            SchemaDifferenceSeverity.Error => "Errors",
            SchemaDifferenceSeverity.Warning => "Warnings",
            SchemaDifferenceSeverity.Info => "Info",
            _ => severity.ToString()
        };

    private static string FormatDifferenceCount(int count) =>
        count == 1 ? "1 difference" : $"{count} differences";

    private static string FormatDifferenceSummary(IReadOnlyList<SchemaDifference> differences)
    {
        var parts = new List<string>();
        AddSeverityCount(parts, differences, SchemaDifferenceSeverity.Error, "error", "errors");
        AddSeverityCount(parts, differences, SchemaDifferenceSeverity.Warning, "warning", "warnings");
        AddSeverityCount(parts, differences, SchemaDifferenceSeverity.Info, "info", "info");

        return string.Join(", ", parts);
    }

    private static void AddSeverityCount(
        List<string> parts,
        IReadOnlyList<SchemaDifference> differences,
        SchemaDifferenceSeverity severity,
        string singular,
        string plural)
    {
        var count = differences.Count(difference => difference.Severity == severity);
        if (count == 0)
            return;

        parts.Add(count == 1 ? $"1 {singular}" : $"{count} {plural}");
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
            issues = result.Issues.Select(CreateIssueJson),
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

    private static void WriteValidationFailureJson(IReadOnlyList<DataLinqDiagnosticIssue> issues)
    {
        var payload = new
        {
            hasIssues = issues.Count > 0,
            issues = issues.Select(CreateIssueJson),
            hasDifferences = false,
            differences = Array.Empty<object>()
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void WriteValidationBatchJson(
        IReadOnlyList<SchemaValidationRunResult> results,
        IReadOnlyList<ValidationBatchFailure> failures)
    {
        var payload = new
        {
            hasFailures = failures.Count > 0,
            hasDifferences = results.Any(result => result.HasDifferences),
            results = results.Select(result => new
            {
                database = result.DatabaseName,
                databaseType = result.DatabaseType.ToString(),
                dataSource = result.DataSourceName,
                modelTableCount = result.ModelTableCount,
                databaseTableCount = result.DatabaseTableCount,
                hasDifferences = result.HasDifferences,
                issues = result.Issues.Select(CreateIssueJson),
                differences = result.Differences.Select(difference => new
                {
                    kind = difference.Kind.ToString(),
                    severity = difference.Severity.ToString(),
                    safety = difference.Safety.ToString(),
                    path = difference.Path,
                    message = difference.Message
                })
            }),
            failures = failures.Select(failure => new
            {
                target = failure.Target == null
                    ? null
                    : new
                    {
                        config = failure.Target.ConfigPath,
                        database = failure.Target.DatabaseName,
                        databaseType = failure.Target.Provider.ToString(),
                        dataSource = failure.Target.DataSourceName
                    },
                issues = failure.Issues.Select(CreateIssueJson)
            })
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static string FormatTargetIdentity(CliConfigTargetIdentity identity) =>
        $"{identity.ConfigPath} :: {identity.DatabaseName} [{identity.Provider}] ({identity.DataSourceName})";

    private static IReadOnlyList<DataLinqDiagnosticIssue> CreateIssues(object failure)
    {
        if (failure is IDLOptionFailure optionFailure)
            return DataLinqDiagnosticIssue.FromFailure(optionFailure);

        return
        [
            new DataLinqDiagnosticIssue(
                DataLinqDiagnosticSeverity.Error,
                DLFailureType.Unspecified,
                failure.ToString() ?? "Unknown DataLinq failure.")
        ];
    }

    private static object CreateIssueJson(DataLinqDiagnosticIssue issue)
    {
        SourceLinePosition? linePosition = null;
        if (issue.SourceLocation.HasValue &&
            issue.SourceLocation.Value.Span.HasValue &&
            TryReadSourceText(issue.SourceLocation.Value, out var sourceText) &&
            SourceLocationFormatter.TryGetLinePosition(
                sourceText,
                issue.SourceLocation.Value.Span.Value,
                out var resolvedLinePosition))
        {
            linePosition = resolvedLinePosition;
        }

        object? location = null;
        if (issue.SourceLocation.HasValue)
        {
            var sourceLocation = issue.SourceLocation.Value;
            location = new
            {
                file = sourceLocation.File.FullPath,
                start = sourceLocation.Span?.Start,
                length = sourceLocation.Span?.Length,
                line = linePosition?.StartLine,
                column = linePosition?.StartColumn,
                endLine = linePosition?.EndLine,
                endColumn = linePosition?.EndColumn
            };
        }

        return new
        {
            severity = issue.Severity.ToString(),
            failureType = issue.FailureType.ToString(),
            message = issue.Message,
            location,
            objectPath = issue.ObjectPath,
            context = issue.ContextMessages
        };
    }

    private static bool TryReadSourceText(SourceLocation sourceLocation, out string sourceText)
    {
        try
        {
            if (File.Exists(sourceLocation.File.FullPath))
            {
                sourceText = File.ReadAllText(sourceLocation.File.FullPath);
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        sourceText = "";
        return false;
    }

    private static void RegisterProviders()
    {
        MySQLProvider.RegisterProvider();
        MariaDBProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();
    }

    private static void ResetState()
    {
        ConfigPath = DefaultConfigPath;
        ConfigFile = null!;
    }

    private static string GetCliVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private sealed record CommonOptionSet(
        Option<bool> VerboseOption,
        Option<string?> ConfigOption);

    private sealed record TargetOptionSet(
        CommonOptionSet CommonOptions,
        Option<string?> DataSourceOption,
        Option<string?> DatabaseOption,
        Option<string?> ProviderOption);

    private sealed record BatchTargetOptionSet(
        TargetOptionSet TargetOptions,
        Option<bool> AllOption,
        Option<bool> RecursiveOption);

    private sealed record ConfigListOptionSet(
        CommonOptionSet CommonOptions,
        Option<bool> RecursiveOption);

    private sealed record ModelGenerationOptionSet(
        BatchTargetOptionSet TargetOptions,
        Option<bool> FreshOption,
        Option<bool> OverwriteTypesOption,
        Option<bool> StampGeneratedHeaderOption);

    internal class Options
    {
        public bool Verbose { get; set; }

        public string? ConfigPath { get; set; }
    }

    internal class CreateOptions : Options
    {
        public string? DataSource { get; set; }

        public string? Database { get; set; }

        public string? Provider { get; set; }
    }

    internal sealed class CreateDatabaseOptions : CreateOptions;

    internal sealed class CreateSqlOptions : CreateOptions
    {
        public string OutputFile { get; set; } = "";
    }

    internal sealed class CreateModelsOptions : CreateOptions
    {
        public bool All { get; set; }

        public bool Recursive { get; set; }

        public bool Fresh { get; set; }

        public bool OverwriteTypes { get; set; }

        public bool StampGeneratedHeader { get; set; }
    }

    internal sealed class ValidateOptions : CreateOptions
    {
        public bool All { get; set; }

        public bool Recursive { get; set; }

        public string Format { get; set; } = "text";
    }

    internal sealed class DiffOptions : CreateOptions
    {
        public string OutputFile { get; set; } = "";
    }

    internal sealed class ListOptions : Options
    {
        public bool Recursive { get; set; }
    }

    internal sealed class ConfigSchemaOptions : Options
    {
        public string OutputFile { get; set; } = "";
    }

    private enum ValidationOutput
    {
        Text,
        Json
    }

    private sealed record ValidationBatchFailure(
        CliConfigTargetIdentity? Target,
        IReadOnlyList<DataLinqDiagnosticIssue> Issues);
}
