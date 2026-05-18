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
    private static SecretResolutionContext SecretContext = null!;

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
        var parseResult = rootCommand.Parse(args.Length == 0 ? ["--help"] : args);
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
        configCommand.Subcommands.Add(CreateConfigInitCommand());
        configCommand.Subcommands.Add(CreateConfigListCommand());
        configCommand.Subcommands.Add(CreateConfigSchemaCommand());

        var secretsCommand = new Command("secrets", "Manage DataLinq local secrets.");
        secretsCommand.Subcommands.Add(CreateSecretsListCommand());
        secretsCommand.Subcommands.Add(CreateSecretsSetCommand());
        secretsCommand.Subcommands.Add(CreateSecretsRemoveCommand());

        rootCommand.Subcommands.Add(generateCommand);
        rootCommand.Subcommands.Add(databaseCommand);
        rootCommand.Subcommands.Add(CreateValidateCommand());
        rootCommand.Subcommands.Add(CreateDiffCommand());
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(secretsCommand);
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

    private static Command CreateConfigInitCommand()
    {
        var command = new Command("init", "Interactively create or complete DataLinq config files.");
        var options = AddCommonOptions(command);

        command.SetAction(parseResult =>
        {
            var initOptions = ReadConfigInitOptions(parseResult, options);
            Environment.ExitCode = ExecuteConfigInit(initOptions);
        });

        return command;
    }

    private static Command CreateConfigSchemaCommand()
    {
        var command = new Command("schema", "Write the DataLinq JSON Schema for config files.");
        var commonOptions = AddCommonOptions(command);
        var outputOption = new Option<string?>("--output")
        {
            Description = "Path to schema output file. If omitted, datalinq.schema.json is written next to the selected config."
        };
        outputOption.Aliases.Add("-o");
        var stdoutOption = new Option<bool>("--stdout")
        {
            Description = "Write the schema JSON to stdout instead of a file."
        };
        command.Options.Add(outputOption);
        command.Options.Add(stdoutOption);

        command.SetAction(parseResult =>
        {
            var options = ReadConfigSchemaOptions(parseResult, commonOptions, outputOption, stdoutOption);
            Environment.ExitCode = ExecuteConfigSchema(options);
        });

        return command;
    }

    private static Command CreateSecretsListCommand()
    {
        var command = new Command("list", "List DataLinq local secret names.");
        command.SetAction(_ => Environment.ExitCode = ExecuteSecretsList());
        return command;
    }

    private static Command CreateSecretsSetCommand()
    {
        var command = new Command("set", "Set a DataLinq local secret value.");
        var nameArgument = new Argument<string>("name")
        {
            Description = "Secret name, such as datalinq/AppDb/password."
        };
        command.Arguments.Add(nameArgument);

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArgument) ?? "";
            Environment.ExitCode = ExecuteSecretsSet(name);
        });

        return command;
    }

    private static Command CreateSecretsRemoveCommand()
    {
        var command = new Command("remove", "Remove a DataLinq local secret.");
        var nameArgument = new Argument<string>("name")
        {
            Description = "Secret name to remove."
        };
        command.Arguments.Add(nameArgument);

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArgument) ?? "";
            Environment.ExitCode = ExecuteSecretsRemove(name);
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

    private static ConfigInitOptions ReadConfigInitOptions(ParseResult parseResult, CommonOptionSet options)
    {
        var initOptions = new ConfigInitOptions();
        ApplyCommonOptions(initOptions, parseResult, options);
        return initOptions;
    }

    private static ConfigSchemaOptions ReadConfigSchemaOptions(
        ParseResult parseResult,
        CommonOptionSet commonOptions,
        Option<string?> outputOption,
        Option<bool> stdoutOption)
    {
        var schemaOptions = new ConfigSchemaOptions
        {
            OutputFile = parseResult.GetValue(outputOption) ?? "",
            Stdout = parseResult.GetValue(stdoutOption)
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
                Console.WriteLine($"{connection.Type} ({Redact(connection.DataSourceName)})");

            Console.WriteLine();
        }
    }

    private static int ExecuteConfigSchema(ConfigSchemaOptions options)
    {
        if (!TryValidateConfigSchemaOptions(options))
            return 2;

        var schemaJson = CliConfigSchema.ReadSchemaJson();
        if (ShouldWriteConfigSchemaToStdout(options))
        {
            Console.Write(schemaJson);
            return 0;
        }

        var outputPath = ResolveConfigSchemaOutputPath(options);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, schemaJson);
        Console.WriteLine($"Wrote DataLinq config schema to {outputPath}");

        return 0;
    }

    private static bool TryValidateConfigSchemaOptions(ConfigSchemaOptions options)
    {
        if (options.Stdout &&
            !string.IsNullOrWhiteSpace(options.OutputFile) &&
            !IsStdoutOutputPath(options.OutputFile))
        {
            ConsoleDiagnosticWriter.WriteError("InvalidArgument", "Use either --stdout or --output, not both.");
            return false;
        }

        return true;
    }

    private static bool ShouldWriteConfigSchemaToStdout(ConfigSchemaOptions options) =>
        options.Stdout || IsStdoutOutputPath(options.OutputFile);

    private static bool IsStdoutOutputPath(string? outputFile) =>
        string.Equals(outputFile, "-", StringComparison.Ordinal);

    private static string ResolveConfigSchemaOutputPath(ConfigSchemaOptions options) =>
        !string.IsNullOrWhiteSpace(options.OutputFile)
            ? Path.GetFullPath(options.OutputFile)
            : ResolveDefaultConfigSchemaOutputPath(ConfigPath);

    private static string ResolveDefaultConfigSchemaOutputPath(string configPath)
    {
        var fullPath = Path.GetFullPath(configPath);
        string directory;

        if (Directory.Exists(fullPath))
        {
            directory = fullPath;
        }
        else
        {
            var fileName = Path.GetFileName(fullPath);
            directory = fileName.Equals("datalinq.json", StringComparison.OrdinalIgnoreCase) ||
                        Path.HasExtension(fullPath)
                ? Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory()
                : fullPath;
        }

        return Path.GetFullPath(Path.Combine(directory, "datalinq.schema.json"));
    }

    private static int ExecuteConfigInit(ConfigInitOptions options)
    {
        var state = CliConfigInit.DetectState(ConfigPath);
        Console.WriteLine("DataLinq config init");
        Console.WriteLine($"Main config: {state.Paths.MainConfigPath}");
        Console.WriteLine($"User config: {state.Paths.UserConfigPath}");
        Console.WriteLine();

        return state.Mode switch
        {
            ConfigInitMode.NewProject => ExecuteNewProjectInit(state),
            ConfigInitMode.CompleteUserConfig => ExecuteMissingUserConfigInit(state, options),
            ConfigInitMode.InspectExisting => ExecuteInspectExistingInit(state, options),
            ConfigInitMode.RepairOrphanedUserConfig => ExecuteRepairOrphanedUserConfigInit(state),
            _ => 2
        };
    }

    private static int ExecuteNewProjectInit(ConfigInitState state)
    {
        var database = PromptDatabaseInput("AppDb", "AppDb", "Models", "Models", "SQLite", "app.db");
        var addGitignore = Confirm("Add datalinq.user.json to .gitignore?", defaultValue: true);
        var plan = CliConfigInit.CreateNewProjectPlan(state.Paths, database, addGitignore);
        return PreviewConfirmAndApply(plan);
    }

    private static int ExecuteMissingUserConfigInit(ConfigInitState state, Options options)
    {
        if (!CliConfigLoader.TryRead(
            state.Paths.MainConfigPath,
            options.Verbose ? ConsoleDiagnosticWriter.WriteLogLine : _ => { },
            out var config,
            out var failure))
        {
            ConsoleDiagnosticWriter.WriteFailure(failure);
            return 2;
        }

        var connections = new List<ConfigInitConnectionInput>();
        foreach (var database in config.Databases)
        {
            if (!Confirm($"Configure local connection for database '{database.Name}'?", defaultValue: true))
                continue;

            var defaultConnection = database.Connections.FirstOrDefault();
            var defaultProvider = defaultConnection?.Type == DatabaseType.Unknown
                ? "SQLite"
                : defaultConnection?.Type.ToString() ?? "SQLite";
            var defaultDataSource = defaultConnection?.DataSourceName ?? database.Name;
            connections.Add(PromptConnectionInput(database.Name, defaultProvider, defaultDataSource));
        }

        if (connections.Count == 0)
        {
            Console.WriteLine("No local connections selected. No files changed.");
            return 0;
        }

        var addGitignore = Confirm("Add datalinq.user.json to .gitignore?", defaultValue: true);
        var plan = CliConfigInit.CreateMissingUserConfigPlan(state.Paths, connections, addGitignore);
        return PreviewConfirmAndApply(plan);
    }

    private static int ExecuteInspectExistingInit(ConfigInitState state, Options options)
    {
        if (!CliConfigLoader.TryRead(
            state.Paths.MainConfigPath,
            options.Verbose ? ConsoleDiagnosticWriter.WriteLogLine : _ => { },
            out var config,
            out var failure))
        {
            ConsoleDiagnosticWriter.WriteFailure(failure);
            return 2;
        }

        var plan = CliConfigInit.CreateInspectExistingPlan(state, config);
        WritePlanPreview(plan);
        return 0;
    }

    private static int ExecuteSecretsList()
        => CliSecretCommandService.List(
            SecretContext.LocalSecrets,
            Console.WriteLine,
            ConsoleDiagnosticWriter.WriteError);

    private static int ExecuteSecretsSet(string name)
        => CliSecretCommandService.Set(
            SecretContext.LocalSecrets,
            SecretContext.Prompt,
            name,
            Console.WriteLine,
            ConsoleDiagnosticWriter.WriteError);

    private static int ExecuteSecretsRemove(string name)
        => CliSecretCommandService.Remove(
            SecretContext.LocalSecrets,
            name,
            secretName => Confirm($"Remove secret '{secretName}'?", defaultValue: false),
            Console.WriteLine,
            ConsoleDiagnosticWriter.WriteError);

    private static int ExecuteRepairOrphanedUserConfigInit(ConfigInitState state)
    {
        ConsoleDiagnosticWriter.WriteWarning(
            "OrphanedUserConfig",
            "Found datalinq.user.json without a matching datalinq.json.");
        if (!Confirm("Create a new datalinq.json without changing the existing user file?", defaultValue: false))
        {
            Console.WriteLine("No files changed.");
            return 0;
        }

        var database = PromptDatabaseInput("AppDb", "AppDb", "Models", "Models", "SQLite", "app.db");
        var plan = CliConfigInit.CreateMainConfigOnlyPlan(state.Paths, database);
        return PreviewConfirmAndApply(plan);
    }

    private static ConfigInitDatabaseInput PromptDatabaseInput(
        string defaultName,
        string defaultCsType,
        string defaultNamespace,
        string defaultModelDirectory,
        string defaultProvider,
        string defaultDataSource)
    {
        var name = Prompt("Database config name", defaultName);
        var csType = Prompt("C# database type", string.IsNullOrWhiteSpace(defaultCsType) ? name : defaultCsType);
        var namespaceName = Prompt("Model namespace", defaultNamespace);
        var modelDirectory = Prompt("Model directory", defaultModelDirectory);
        var useNullableReferenceTypes = Confirm("Enable nullable reference types in generated models?", defaultValue: true);
        var useFileScopedNamespaces = Confirm("Use file-scoped namespaces?", defaultValue: true);
        var connection = PromptConnectionInput(name, defaultProvider, defaultDataSource);

        return new ConfigInitDatabaseInput(
            name,
            csType,
            namespaceName,
            modelDirectory,
            useNullableReferenceTypes,
            useFileScopedNamespaces,
            connection);
    }

    private static ConfigInitConnectionInput PromptConnectionInput(
        string databaseName,
        string defaultProvider,
        string defaultDataSource)
    {
        var provider = Prompt("Provider (SQLite, MySQL, MariaDB)", defaultProvider);
        var dataSource = Prompt("Local data source name", defaultDataSource);
        var defaultConnectionString = CliConfigInit.CreateDefaultConnectionString(provider, dataSource);
        var connectionString = Prompt("Local connection string", defaultConnectionString);

        return new ConfigInitConnectionInput(databaseName, provider, dataSource, connectionString);
    }

    private static int PreviewConfirmAndApply(ConfigInitPlan plan)
    {
        WritePlanPreview(plan);
        if (!plan.HasWrites)
            return 0;

        if (!Confirm("Apply this plan?", defaultValue: true))
        {
            Console.WriteLine("No files changed.");
            return 0;
        }

        try
        {
            CliConfigInit.Apply(plan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ConsoleDiagnosticWriter.WriteError("InitFailed", exception.Message);
            return 2;
        }

        Console.WriteLine("Config init complete.");
        Console.WriteLine("Next step: datalinq generate models --database <name> --provider <provider>");
        return 0;
    }

    private static void WritePlanPreview(ConfigInitPlan plan)
    {
        Console.WriteLine("Plan:");
        foreach (var line in plan.PreviewLines)
            Console.WriteLine($"  - {line}");

        Console.WriteLine();
    }

    private static string Prompt(string label, string defaultValue)
    {
        Console.Write($"{label} [{defaultValue}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input)
            ? defaultValue
            : input.Trim();
    }

    private static bool Confirm(string label, bool defaultValue)
    {
        var suffix = defaultValue ? "Y/n" : "y/N";
        Console.Write($"{label} [{suffix}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            return defaultValue;

        return input.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
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
            options.Verbose ? ConsoleDiagnosticWriter.WriteLogLine : _ => { },
            SecretContext);

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
                : ConsoleDiagnosticWriter.WriteLogLine,
            SecretContext);

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
        if (!CliConfigLoader.TryRead(ConfigPath, configLog, out var config, out var readFailure, SecretContext))
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
        Console.WriteLine($"Validation target: {result.DatabaseName} [{result.DatabaseType}] ({Redact(result.DataSourceName)})");
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
                Console.WriteLine($"    {Redact(difference.Message)}");
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
            dataSource = Redact(result.DataSourceName),
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
                message = Redact(difference.Message)
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
                dataSource = Redact(result.DataSourceName),
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
                    message = Redact(difference.Message)
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
                        dataSource = Redact(failure.Target.DataSourceName)
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
        $"{identity.ConfigPath} :: {identity.DatabaseName} [{identity.Provider}] ({Redact(identity.DataSourceName)})";

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
            message = Redact(issue.Message),
            location,
            objectPath = issue.ObjectPath,
            context = issue.ContextMessages.Select(Redact)
        };
    }

    private static string Redact(string? value) =>
        ConsoleDiagnosticWriter.Redactor.Redact(value);

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
        SecretContext = SecretResolutionContext.CreateDefault();
        ConsoleDiagnosticWriter.Redactor = SecretContext.Redactor;
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

    internal sealed class ConfigInitOptions : Options;

    internal sealed class ConfigSchemaOptions : Options
    {
        public string OutputFile { get; set; } = "";

        public bool Stdout { get; set; }
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
