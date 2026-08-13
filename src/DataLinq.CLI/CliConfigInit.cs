using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataLinq.Config;
using DataLinq.Metadata;

namespace DataLinq.CLI;

internal enum ConfigInitMode
{
    NewProject,
    CompleteUserConfig,
    InspectExisting,
    RepairOrphanedUserConfig
}

internal sealed record ConfigInitPaths(
    string BaseDirectory,
    string MainConfigPath,
    string UserConfigPath);

internal sealed record ConfigInitState(
    ConfigInitPaths Paths,
    bool MainConfigExists,
    bool UserConfigExists)
{
    public ConfigInitMode Mode =>
        (MainConfigExists, UserConfigExists) switch
        {
            (false, false) => ConfigInitMode.NewProject,
            (true, false) => ConfigInitMode.CompleteUserConfig,
            (true, true) => ConfigInitMode.InspectExisting,
            (false, true) => ConfigInitMode.RepairOrphanedUserConfig
        };
}

internal sealed record ConfigInitConnectionInput(
    string DatabaseName,
    string Provider,
    string DataSourceName,
    string ConnectionString);

internal sealed record ConfigInitDatabaseInput(
    string Name,
    string CsType,
    string Namespace,
    string ModelDirectory,
    bool UseNullableReferenceTypes,
    bool UseFileScopedNamespaces,
    ConfigInitConnectionInput Connection);

internal sealed record ConfigInitFileWrite(
    string Path,
    string Contents,
    bool Overwrite);

internal sealed record ConfigInitGitignoreUpdate(
    string GitignorePath,
    string Entry);

internal sealed record ConfigInitPlan(
    ConfigInitMode Mode,
    IReadOnlyList<ConfigInitFileWrite> FileWrites,
    ConfigInitGitignoreUpdate? GitignoreUpdate,
    IReadOnlyList<string> PreviewLines)
{
    public bool HasWrites => FileWrites.Count > 0 || GitignoreUpdate != null;
}

internal static class CliConfigInit
{
    public static ConfigInitState DetectState(string configPath)
    {
        var mainConfigPath = CliConfigDiscovery.ResolveConfigFilePath(configPath);
        var baseDirectory = Path.GetDirectoryName(mainConfigPath) ?? Directory.GetCurrentDirectory();
        var userConfigPath = CreateUserConfigPath(mainConfigPath);

        return new ConfigInitState(
            new ConfigInitPaths(baseDirectory, mainConfigPath, userConfigPath),
            File.Exists(mainConfigPath),
            File.Exists(userConfigPath));
    }

    public static ConfigInitPlan CreateNewProjectPlan(
        ConfigInitPaths paths,
        ConfigInitDatabaseInput database,
        bool addGitignore)
    {
        var mainConfig = SerializeMainConfig([database]);
        var userConfig = SerializeUserConfig([database.Connection]);

        return CreatePlan(
            ConfigInitMode.NewProject,
            paths,
            [
                new ConfigInitFileWrite(paths.MainConfigPath, mainConfig, Overwrite: false),
                new ConfigInitFileWrite(paths.UserConfigPath, userConfig, Overwrite: false)
            ],
            addGitignore);
    }

    public static ConfigInitPlan CreateMainConfigOnlyPlan(
        ConfigInitPaths paths,
        ConfigInitDatabaseInput database)
    {
        return CreatePlan(
            ConfigInitMode.RepairOrphanedUserConfig,
            paths,
            [new ConfigInitFileWrite(paths.MainConfigPath, SerializeMainConfig([database]), Overwrite: false)],
            addGitignore: false);
    }

    public static ConfigInitPlan CreateMissingUserConfigPlan(
        ConfigInitPaths paths,
        IReadOnlyList<ConfigInitConnectionInput> connections,
        bool addGitignore)
    {
        return CreatePlan(
            ConfigInitMode.CompleteUserConfig,
            paths,
            [new ConfigInitFileWrite(paths.UserConfigPath, SerializeUserConfig(connections), Overwrite: false)],
            addGitignore);
    }

    public static ConfigInitPlan CreateInspectExistingPlan(ConfigInitState state, DataLinqConfig config)
    {
        var previewLines = new List<string>
        {
            $"Found existing {state.Paths.MainConfigPath}.",
            $"Found existing {state.Paths.UserConfigPath}.",
            "No files will be changed."
        };

        foreach (var database in config.Databases)
            previewLines.Add($"Database {database.Name}: {database.Connections.Count} connection(s).");

        return new ConfigInitPlan(ConfigInitMode.InspectExisting, [], null, previewLines);
    }

    public static void Apply(ConfigInitPlan plan)
    {
        foreach (var write in plan.FileWrites)
        {
            if (!write.Overwrite && File.Exists(write.Path))
                throw new IOException($"Refusing to overwrite existing file '{write.Path}'.");

            var directory = Path.GetDirectoryName(write.Path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(write.Path, write.Contents);
        }

        if (plan.GitignoreUpdate != null)
            ApplyGitignoreUpdate(plan.GitignoreUpdate);
    }

    public static ConfigInitConnectionInput CreateDefaultConnectionInput(
        string databaseName,
        DatabaseType provider,
        string? dataSourceName = null)
    {
        var providerName = provider == DatabaseType.Unknown
            ? "SQLite"
            : provider.ToString();
        var effectiveDataSource = string.IsNullOrWhiteSpace(dataSourceName)
            ? CreateDefaultDataSourceName(databaseName, providerName)
            : dataSourceName;

        return new ConfigInitConnectionInput(
            databaseName,
            providerName,
            effectiveDataSource,
            CreateDefaultConnectionString(providerName, effectiveDataSource));
    }

    public static string CreateDefaultConnectionString(string provider, string dataSourceName) =>
        provider switch
        {
            "SQLite" => $"Data Source={dataSourceName};",
            "MySQL" => $"Server=localhost;Database={dataSourceName};User ID=root;Password=${{prompt:{dataSourceName} password}};",
            "MariaDB" => $"Server=localhost;Database={dataSourceName};User ID=root;Password=${{prompt:{dataSourceName} password}};",
            _ => $"Data Source={dataSourceName};"
        };

    private static ConfigInitPlan CreatePlan(
        ConfigInitMode mode,
        ConfigInitPaths paths,
        IReadOnlyList<ConfigInitFileWrite> fileWrites,
        bool addGitignore)
    {
        var previewLines = new List<string>();
        previewLines.AddRange(fileWrites.Select(write => $"Create {write.Path}"));

        var gitignoreUpdate = addGitignore
            ? TryCreateGitignoreUpdate(paths.UserConfigPath)
            : null;
        if (gitignoreUpdate != null)
            previewLines.Add($"Append '{gitignoreUpdate.Entry}' to {gitignoreUpdate.GitignorePath}");

        return new ConfigInitPlan(mode, fileWrites, gitignoreUpdate, previewLines);
    }

    private static string CreateUserConfigPath(string mainConfigPath)
    {
        var directory = Path.GetDirectoryName(mainConfigPath) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileNameWithoutExtension(mainConfigPath);
        return Path.Combine(directory, $"{fileName}.user.json");
    }

    private static string CreateDefaultDataSourceName(string databaseName, string provider) =>
        provider == "SQLite"
            ? $"{databaseName}.db"
            : databaseName;

    private static ConfigInitGitignoreUpdate? TryCreateGitignoreUpdate(string userConfigPath)
    {
        var gitRoot = FindGitRoot(Path.GetDirectoryName(userConfigPath) ?? Directory.GetCurrentDirectory());
        if (gitRoot == null)
            return null;

        var entry = Path.GetRelativePath(gitRoot, userConfigPath).Replace('\\', '/');
        var gitignorePath = Path.Combine(gitRoot, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            var existingEntries = File.ReadAllLines(gitignorePath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
            if (existingEntries.Contains(entry, StringComparer.Ordinal))
                return null;
        }

        return new ConfigInitGitignoreUpdate(gitignorePath, entry);
    }

    private static string? FindGitRoot(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    private static void ApplyGitignoreUpdate(ConfigInitGitignoreUpdate update)
    {
        var directory = Path.GetDirectoryName(update.GitignorePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var prefix = "";
        if (File.Exists(update.GitignorePath))
        {
            var current = File.ReadAllText(update.GitignorePath);
            if (current.Length > 0 && !current.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                prefix = Environment.NewLine;
        }

        File.AppendAllText(update.GitignorePath, $"{prefix}{update.Entry}{Environment.NewLine}");
    }

    private static string SerializeMainConfig(IReadOnlyList<ConfigInitDatabaseInput> databases) =>
        Serialize(new InitConfigFile
        {
            Schema = CliConfigSchema.SchemaUrl,
            Databases = databases
                .Select(database => new InitDatabaseConfig
                {
                    Name = database.Name,
                    CsType = database.CsType,
                    Namespace = database.Namespace,
                    ModelDirectory = database.ModelDirectory,
                    UseNullableReferenceTypes = database.UseNullableReferenceTypes,
                    UseFileScopedNamespaces = database.UseFileScopedNamespaces
                })
                .ToList()
        });

    private static string SerializeUserConfig(IReadOnlyList<ConfigInitConnectionInput> connections) =>
        Serialize(new InitConfigFile
        {
            Databases = connections
                .Select(connection => new InitDatabaseConfig
                {
                    Name = connection.DatabaseName,
                    Connections =
                    [
                        new InitConnectionConfig
                        {
                            Type = connection.Provider,
                            DataSourceName = connection.DataSourceName,
                            ConnectionString = connection.ConnectionString
                        }
                    ]
                })
                .ToList()
        });

    private static string Serialize(InitConfigFile config) =>
        JsonSerializer.Serialize(config, ConfigInitJsonContext.Default.InitConfigFile) + Environment.NewLine;

    internal sealed record InitConfigFile
    {
        [JsonPropertyName("$schema")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Schema { get; init; }

        public List<InitDatabaseConfig> Databases { get; init; } = [];
    }

    internal sealed record InitDatabaseConfig
    {
        public string Name { get; init; } = "";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CsType { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Namespace { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ModelDirectory { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseNullableReferenceTypes { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseFileScopedNamespaces { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<InitConnectionConfig>? Connections { get; init; }
    }

    internal sealed record InitConnectionConfig
    {
        public string Type { get; init; } = "";
        public string DataSourceName { get; init; } = "";
        public string ConnectionString { get; init; } = "";
    }
}

[JsonSerializable(typeof(CliConfigInit.InitConfigFile))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class ConfigInitJsonContext : JsonSerializerContext;
