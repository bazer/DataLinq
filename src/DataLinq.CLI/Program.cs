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
        var targetOptions = AddTargetOptions(command);
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
        var options = AddCommonOptions(command);

        command.SetAction(parseResult =>
        {
            var listOptions = ReadListOptions(parseResult, options);
            Environment.ExitCode = ExecuteList(listOptions);
        });

        return command;
    }

    private static ModelGenerationOptionSet AddModelGenerationOptions(Command command)
    {
        var targetOptions = AddTargetOptions(command);
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
        var createOptions = ReadTargetOptions<CreateModelsOptions>(parseResult, options.TargetOptions);
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
        TargetOptionSet targetOptions,
        Option<string> formatOption)
    {
        var validateOptions = ReadTargetOptions<ValidateOptions>(parseResult, targetOptions);
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

    private static ListOptions ReadListOptions(ParseResult parseResult, CommonOptionSet options)
    {
        var listOptions = new ListOptions();
        ApplyCommonOptions(listOptions, parseResult, options);
        return listOptions;
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
        if (ReadConfig() == false)
            return 2;

        Console.WriteLine("Databases in config:");
        foreach (var db in ConfigFile.Databases)
        {
            Console.WriteLine(db.Name);
            Console.WriteLine("Connections:");
            foreach (var connection in db.Connections)
                Console.WriteLine($"{connection.Type} ({connection.DataSourceName})");

            Console.WriteLine();

            var reader = new ModelReader(ConsoleDiagnosticWriter.WriteLogLine);
            var result = reader.Read(ConfigFile, ConfigBasePath);

            if (result.HasFailed)
            {
                ConsoleDiagnosticWriter.WriteFailure(result.Failure);
                return 2;
            }
        }

        return 0;
    }

    private static int ExecuteCreateModels(CreateModelsOptions options)
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
        var config = DataLinqConfig.FindAndReadConfigs(ConfigPath, configLog);

        if (config.HasFailed)
        {
            failure = config.Failure;
            return false;
        }

        configLog("");
        ConfigFile = config.Value;
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

    private sealed record ModelGenerationOptionSet(
        TargetOptionSet TargetOptions,
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
        public bool Fresh { get; set; }

        public bool OverwriteTypes { get; set; }

        public bool StampGeneratedHeader { get; set; }
    }

    internal sealed class ValidateOptions : CreateOptions
    {
        public string Format { get; set; } = "text";
    }

    internal sealed class DiffOptions : CreateOptions
    {
        public string OutputFile { get; set; } = "";
    }

    internal sealed class ListOptions : Options;

    private enum ValidationOutput
    {
        Text,
        Json
    }
}
