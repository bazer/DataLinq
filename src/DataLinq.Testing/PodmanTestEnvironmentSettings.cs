using System;
using System.Collections.Generic;
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
    string ContainerPrefix,
    string ProfileId,
    string Host,
    string AdminUser,
    string AdminPassword,
    string ApplicationUser,
    string ApplicationPassword,
    IReadOnlyDictionary<string, int> TargetPorts,
    IReadOnlyList<string> AvailableTargetIds)
{
    public const string ContainerPrefixEnvironmentVariable = "DATALINQ_TEST_CONTAINER_PREFIX";
    public const string HostEnvironmentVariable = "DATALINQ_TEST_DB_HOST";
    public const string AdminUserEnvironmentVariable = "DATALINQ_TEST_DB_ADMIN_USER";
    public const string AdminPasswordEnvironmentVariable = "DATALINQ_TEST_DB_ADMIN_PASSWORD";
    public const string ApplicationUserEnvironmentVariable = "DATALINQ_TEST_DB_APP_USER";
    public const string ApplicationPasswordEnvironmentVariable = "DATALINQ_TEST_DB_APP_PASSWORD";
    public const string EmployeesDatabaseEnvironmentVariable = "DATALINQ_TEST_EMPLOYEES_DB";
    public const string ProviderSetEnvironmentVariable = "DATALINQ_TEST_PROVIDER_SET";
    public const string TargetAliasEnvironmentVariable = "DATALINQ_TEST_TARGET_ALIAS";
    public const string TargetIdsEnvironmentVariable = "DATALINQ_TEST_TARGETS";
    public const string PodmanExecutablePathEnvironmentVariable = "DATALINQ_TEST_PODMAN_PATH";

    private const string LegacyContainerPrefixEnvironmentVariable = "DATALINQ_TEST_PODMAN_POD";

    public static string ResolveContainerPrefix(string fallback) =>
        GetEnvironmentVariable(fallback, ContainerPrefixEnvironmentVariable, LegacyContainerPrefixEnvironmentVariable);

    public static PodmanTestEnvironmentSettings FromEnvironment(string? repositoryRoot = null)
    {
        var root = repositoryRoot ?? RepositoryLayout.FindRepositoryRoot();
        var artifactRoot = Path.Combine(root, "artifacts", "testdata");
        var persistedState = LoadPersistedState(artifactRoot);
        var persistedTargetPorts = persistedState?.Targets?
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && x.Port is not null)
            .GroupBy(x => x.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Port!.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var targetPorts = BuildTargetPortMap(persistedTargetPorts);
        var availableTargetIds = persistedState?.Targets?
            .Select(x => x.Id)
            .OfType<string>()
            .Where(static x => !TestTargetCatalog.IsSQLiteTarget(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();

        return new PodmanTestEnvironmentSettings(
            RepositoryRoot: root,
            ArtifactRoot: artifactRoot,
            ContainerPrefix: ResolveContainerPrefix(persistedState?.ContainerPrefix ?? "datalinq-tests"),
            ProfileId: persistedState?.ProfileId ?? DatabaseServerMatrix.DefaultProfile.Id,
            Host: GetEnvironmentVariable(persistedState?.Host ?? "127.0.0.1", HostEnvironmentVariable),
            AdminUser: GetEnvironmentVariable(persistedState?.AdminUser ?? "datalinq", AdminUserEnvironmentVariable),
            AdminPassword: GetEnvironmentVariable(persistedState?.AdminPassword ?? "datalinq", AdminPasswordEnvironmentVariable),
            ApplicationUser: GetEnvironmentVariable(persistedState?.ApplicationUser ?? "datalinq", ApplicationUserEnvironmentVariable),
            ApplicationPassword: GetEnvironmentVariable(persistedState?.ApplicationPassword ?? "datalinq", ApplicationPasswordEnvironmentVariable),
            TargetPorts: targetPorts,
            AvailableTargetIds: availableTargetIds);
    }

    public DatabaseServerProfile ActiveProfile => DatabaseServerMatrix.GetProfile(ProfileId);
    public string? TargetAlias => GetOptionalEnvironmentVariable(TargetAliasEnvironmentVariable);
    public string ProviderSet => GetEnvironmentVariable(TargetAlias is null ? "fast" : "alias", ProviderSetEnvironmentVariable);

    public IReadOnlyList<string> SelectedTargetIds =>
        ParseTargetIds(Environment.GetEnvironmentVariable(TargetIdsEnvironmentVariable))
        ?? (TargetAlias is null ? null : TestTargetCatalog.ResolveAlias(TargetAlias))
        ?? AvailableTargetIds;

    public int GetPort(TestProviderDescriptor provider)
    {
        if (provider.ServerTarget is null)
            throw new InvalidOperationException($"Provider '{provider.Name}' does not expose a server port.");

        return GetPort(provider.ServerTarget);
    }

    public int GetPort(DatabaseServerTarget target)
    {
        if (TargetPorts.TryGetValue(target.Id, out var port))
            return port;

        return target.HostPort;
    }

    public string CreateAdminConnectionString(DatabaseServerTarget target)
    {
        EnsureTargetIsAvailable(target);

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

    public IReadOnlyList<DatabaseServerTarget> GetAvailableServerTargets()
    {
        var targetIds = AvailableTargetIds.Count > 0
            ? AvailableTargetIds
            : ActiveProfile.Targets.Select(x => x.Id).ToArray();

        return targetIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(DatabaseServerMatrix.GetTarget)
            .ToArray();
    }

    private TestConnectionDefinition CreateServerConnection(DatabaseServerTarget target, string logicalDatabaseName)
    {
        EnsureTargetIsAvailable(target);

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

    private void EnsureTargetIsAvailable(DatabaseServerTarget target)
    {
        if (AvailableTargetIds.Count == 0)
            return;

        if (AvailableTargetIds.Contains(target.Id, StringComparer.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException(
            $"Server target '{target.Id}' is not part of the currently provisioned Podman target set. " +
            $"Available targets: [{string.Join(", ", AvailableTargetIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}].");
    }

    private static IReadOnlyDictionary<string, int> BuildTargetPortMap(IReadOnlyDictionary<string, int> persistedTargetPorts)
    {
        var ports = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in DatabaseServerMatrix.Targets)
        {
            ports[target.Id] = persistedTargetPorts.TryGetValue(target.Id, out var port)
                ? port
                : target.HostPort;
        }

        return ports;
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

    private static string GetEnvironmentVariable(string fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value)
                return value;
        }

        return fallback;
    }

    private static string? GetOptionalEnvironmentVariable(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<string>? ParseTargetIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PersistedPodmanState? LoadPersistedState(string artifactRoot)
    {
        var currentStatePath = Path.Combine(artifactRoot, "testinfra-state.json");
        if (File.Exists(currentStatePath))
        {
            try
            {
                var currentState = JsonSerializer.Deserialize<CurrentPersistedState>(
                    File.ReadAllText(currentStatePath),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (currentState is not null)
                {
                    return new PersistedPodmanState(
                        ContainerPrefix: null,
                        ProfileId: null,
                        Host: currentState.Host,
                        AdminUser: currentState.AdminUser,
                        AdminPassword: currentState.AdminPassword,
                        ApplicationUser: currentState.ApplicationUser,
                        ApplicationPassword: currentState.ApplicationPassword,
                        Targets: currentState.Targets?
                            .Select(x => new PersistedTargetState(x.Id, x.Port))
                            .ToArray());
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private sealed record PersistedPodmanState(
        string? ContainerPrefix,
        string? ProfileId,
        string? Host,
        string? AdminUser,
        string? AdminPassword,
        string? ApplicationUser,
        string? ApplicationPassword,
        PersistedTargetState[]? Targets);

    private sealed record PersistedTargetState(
        string? Id,
        int? Port);

    private sealed record CurrentPersistedState(
        string? AliasName,
        string? Host,
        string? AdminUser,
        string? AdminPassword,
        string? ApplicationUser,
        string? ApplicationPassword,
        CurrentPersistedTargetState[]? Targets);

    private sealed record CurrentPersistedTargetState(
        string? Id,
        int? Port);
}
