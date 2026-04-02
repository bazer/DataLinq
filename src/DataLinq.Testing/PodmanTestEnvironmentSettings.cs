using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Testing;

public sealed record PodmanTestEnvironmentSettings(
    string RepositoryRoot,
    string ArtifactRoot,
    string PodName,
    string ProfileId,
    string Host,
    int MySqlPort,
    int MariaDbPort,
    string AdminUser,
    string AdminPassword,
    string ApplicationUser,
    string ApplicationPassword)
{
    public const string PodNameEnvironmentVariable = "DATALINQ_TEST_PODMAN_POD";
    public const string ProfileEnvironmentVariable = "DATALINQ_TEST_PROFILE";
    public const string HostEnvironmentVariable = "DATALINQ_TEST_DB_HOST";
    public const string MySqlPortEnvironmentVariable = "DATALINQ_TEST_MYSQL_PORT";
    public const string MariaDbPortEnvironmentVariable = "DATALINQ_TEST_MARIADB_PORT";
    public const string AdminUserEnvironmentVariable = "DATALINQ_TEST_DB_ADMIN_USER";
    public const string AdminPasswordEnvironmentVariable = "DATALINQ_TEST_DB_ADMIN_PASSWORD";
    public const string ApplicationUserEnvironmentVariable = "DATALINQ_TEST_DB_APP_USER";
    public const string ApplicationPasswordEnvironmentVariable = "DATALINQ_TEST_DB_APP_PASSWORD";

    public static PodmanTestEnvironmentSettings FromEnvironment(string? repositoryRoot = null)
    {
        var root = repositoryRoot ?? RepositoryLayout.FindRepositoryRoot();
        var artifactRoot = Path.Combine(root, "artifacts", "testdata");
        var persistedState = LoadPersistedState(artifactRoot);

        return new PodmanTestEnvironmentSettings(
            RepositoryRoot: root,
            ArtifactRoot: artifactRoot,
            PodName: GetEnvironmentVariable(PodNameEnvironmentVariable, persistedState?.PodName ?? "datalinq-tests"),
            ProfileId: GetEnvironmentVariable(ProfileEnvironmentVariable, persistedState?.ProfileId ?? "current-lts"),
            Host: GetEnvironmentVariable(HostEnvironmentVariable, persistedState?.Host ?? "127.0.0.1"),
            MySqlPort: GetEnvironmentVariable(MySqlPortEnvironmentVariable, persistedState?.MySqlPort ?? 3307),
            MariaDbPort: GetEnvironmentVariable(MariaDbPortEnvironmentVariable, persistedState?.MariaDbPort ?? 3308),
            AdminUser: GetEnvironmentVariable(AdminUserEnvironmentVariable, persistedState?.AdminUser ?? "datalinq"),
            AdminPassword: GetEnvironmentVariable(AdminPasswordEnvironmentVariable, persistedState?.AdminPassword ?? "datalinq"),
            ApplicationUser: GetEnvironmentVariable(ApplicationUserEnvironmentVariable, persistedState?.ApplicationUser ?? "datalinq"),
            ApplicationPassword: GetEnvironmentVariable(ApplicationPasswordEnvironmentVariable, persistedState?.ApplicationPassword ?? "datalinq"));
    }

    public DatabaseServerProfile ActiveProfile => DatabaseServerMatrix.GetProfile(ProfileId);

    public int GetPort(TestProviderDescriptor provider) => provider.Kind switch
    {
        TestProviderKind.Server when provider.ServerTarget?.Family == DatabaseServerFamily.MySql => MySqlPort,
        TestProviderKind.Server when provider.ServerTarget?.Family == DatabaseServerFamily.MariaDb => MariaDbPort,
        _ => throw new InvalidOperationException($"Provider '{provider.Name}' does not expose a server port.")
    };

    public int GetPort(DatabaseServerTarget target) => target.Family switch
    {
        DatabaseServerFamily.MySql => MySqlPort,
        DatabaseServerFamily.MariaDb => MariaDbPort,
        _ => throw new InvalidOperationException($"Server target '{target.Id}' does not expose a configured port.")
    };

    public string CreateAdminConnectionString(DatabaseServerTarget target)
    {
        if (!ActiveProfile.Targets.Any(x => x.Id == target.Id))
            throw new InvalidOperationException($"Server target '{target.Id}' is not part of the active profile '{ActiveProfile.Id}'.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)GetPort(target),
            UserID = AdminUser,
            Password = AdminPassword,
            Pooling = true,
            MaximumPoolSize = 20,
            CharacterSet = "utf8mb4"
        };

        return builder.ConnectionString;
    }

    public TestConnectionDefinition CreateConnection(TestProviderDescriptor provider, string logicalDatabaseName)
    {
        var normalizedName = NormalizeName(logicalDatabaseName);

        return provider.Kind switch
        {
            TestProviderKind.SQLiteFile => CreateSqliteFileConnection(normalizedName),
            TestProviderKind.SQLiteInMemory => CreateSqliteInMemoryConnection(normalizedName),
            TestProviderKind.Server when provider.ServerTarget is not null => CreateServerConnection(provider.ServerTarget, normalizedName),
            _ => throw new InvalidOperationException($"Unsupported provider kind '{provider.Kind}'.")
        };
    }

    private TestConnectionDefinition CreateServerConnection(DatabaseServerTarget target, string logicalDatabaseName)
    {
        if (!ActiveProfile.Targets.Any(x => x.Id == target.Id))
            throw new InvalidOperationException($"Server target '{target.Id}' is not part of the active profile '{ActiveProfile.Id}'.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)GetPort(target),
            Database = logicalDatabaseName,
            UserID = ApplicationUser,
            Password = ApplicationPassword,
            Pooling = true,
            MaximumPoolSize = 100,
            CharacterSet = "utf8mb4"
        };

        return new TestConnectionDefinition(
            LogicalDatabaseName: logicalDatabaseName,
            DataSourceName: logicalDatabaseName,
            DatabaseType: target.DatabaseType,
            ConnectionString: builder.ConnectionString);
    }

    private TestConnectionDefinition CreateSqliteFileConnection(string logicalDatabaseName)
    {
        var sqliteDirectory = Path.Combine(ArtifactRoot, "sqlite");
        Directory.CreateDirectory(sqliteDirectory);

        var filePath = Path.Combine(sqliteDirectory, $"{logicalDatabaseName}.db");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Cache = SqliteCacheMode.Shared
        };

        return new TestConnectionDefinition(
            LogicalDatabaseName: logicalDatabaseName,
            DataSourceName: logicalDatabaseName,
            DatabaseType: DataLinq.DatabaseType.SQLite,
            ConnectionString: builder.ConnectionString);
    }

    private static TestConnectionDefinition CreateSqliteInMemoryConnection(string logicalDatabaseName)
    {
        var dataSourceName = $"{logicalDatabaseName}_memory";
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dataSourceName,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        };

        return new TestConnectionDefinition(
            LogicalDatabaseName: logicalDatabaseName,
            DataSourceName: dataSourceName,
            DatabaseType: DataLinq.DatabaseType.SQLite,
            ConnectionString: builder.ConnectionString);
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A logical database name must be provided.", nameof(value));

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        var normalized = builder.ToString().Trim('_');
        if (normalized.Length <= 64)
            return normalized;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant()[..12];
        var prefixLength = 64 - hash.Length - 1;
        return $"{normalized[..prefixLength].TrimEnd('_')}_{hash}";
    }

    private static string GetEnvironmentVariable(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value
            : fallback;

    private static int GetEnvironmentVariable(string key, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static PersistedPodmanState? LoadPersistedState(string artifactRoot)
    {
        var statePath = Path.Combine(artifactRoot, "podman-settings.json");
        if (!File.Exists(statePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PersistedPodmanState>(
                File.ReadAllText(statePath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PersistedPodmanState(
        string? PodName,
        string? ProfileId,
        string? Host,
        int? MySqlPort,
        int? MariaDbPort,
        string? AdminUser,
        string? AdminPassword,
        string? ApplicationUser,
        string? ApplicationPassword);
}
