using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Config;
using DataLinq.Metadata;

namespace DataLinq.CLI;

internal sealed record CliTargetFilter(
    string? DatabaseName,
    string? ProviderName);

internal enum CliTargetExpansionMode
{
    Validation,
    ModelGeneration
}

internal sealed record CliConfigTarget(
    string ConfigPath,
    DataLinqConfig Config,
    DataLinqDatabaseConfig Database,
    DataLinqDatabaseConnection Connection)
{
    public CliConfigTargetIdentity Identity { get; } = new(
        ConfigPath,
        Database.Name,
        Connection.Type,
        Connection.DataSourceName);
}

internal sealed record CliConfigTargetIdentity(
    string ConfigPath,
    string DatabaseName,
    DatabaseType Provider,
    string DataSourceName);

internal sealed record CliTargetExpansion(
    int ConfigCount,
    IReadOnlyList<CliConfigTarget> Targets,
    IReadOnlyList<CliTargetExpansionFailure> Failures);

internal sealed record CliTargetExpansionFailure(
    string ConfigPath,
    object Failure);

internal static class CliTargetResolver
{
    public static CliTargetExpansion Expand(
        string configPath,
        CliTargetFilter filter,
        bool recursive,
        Action<string> log,
        SecretResolutionContext? secrets = null,
        CliTargetExpansionMode mode = CliTargetExpansionMode.Validation)
    {
        if (!TryParseProviderFilter(filter.ProviderName, out var provider, out var providerFailure))
        {
            return new CliTargetExpansion(
                1,
                [],
                [new CliTargetExpansionFailure(configPath, providerFailure!)]);
        }

        var configPaths = recursive
            ? CliConfigDiscovery.DiscoverConfigFiles(configPath)
            : [CliConfigDiscovery.ResolveConfigFilePath(configPath)];

        if (recursive && configPaths.Count == 0)
        {
            return new CliTargetExpansion(
                0,
                [],
                [
                    new CliTargetExpansionFailure(
                        CliConfigDiscovery.ResolveDiscoveryRoot(configPath),
                        $"No datalinq.json files were found under '{CliConfigDiscovery.ResolveDiscoveryRoot(configPath)}'.")
                ]);
        }

        var targets = new List<CliConfigTarget>();
        var failures = new List<CliTargetExpansionFailure>();

        foreach (var discoveredConfigPath in configPaths)
        {
            if (!CliConfigLoader.TryRead(discoveredConfigPath, log, out var config, out var failure, secrets))
            {
                failures.Add(new CliTargetExpansionFailure(discoveredConfigPath, failure));
                continue;
            }

            foreach (var database in FilterDatabases(config.Databases, filter.DatabaseName))
            {
                if (database.Connections.Count == 0)
                {
                    failures.Add(new CliTargetExpansionFailure(
                        discoveredConfigPath,
                        $"Database '{database.Name}' has no connections to read from."));
                    continue;
                }

                var matchingConnections = FilterConnections(database.Connections, provider).ToArray();
                if (matchingConnections.Length == 0)
                    continue;

                if (mode == CliTargetExpansionMode.ModelGeneration && matchingConnections.Length > 1)
                {
                    failures.Add(new CliTargetExpansionFailure(
                        discoveredConfigPath,
                        CreateModelGenerationAmbiguityMessage(database, provider, matchingConnections)));
                    continue;
                }

                foreach (var connection in matchingConnections)
                    targets.Add(new CliConfigTarget(discoveredConfigPath, config, database, connection));
            }
        }

        return new CliTargetExpansion(configPaths.Count, targets, failures);
    }

    private static string CreateModelGenerationAmbiguityMessage(
        DataLinqDatabaseConfig database,
        DatabaseType provider,
        IReadOnlyList<DataLinqDatabaseConnection> connections)
    {
        var providerDescription = provider == DatabaseType.Unknown
            ? "multiple connections"
            : $"multiple {provider} connections";
        var dataSources = string.Join(", ", connections.Select(connection => $"'{connection.DataSourceName}'"));

        return $"Database '{database.Name}' has {providerDescription} ({dataSources}) that would write to the same ModelDirectory. Pass --provider to select one provider or split the targets before running batch model generation.";
    }

    private static bool TryParseProviderFilter(
        string? providerName,
        out DatabaseType provider,
        out object? failure)
    {
        provider = ConfigReader.ParseDatabaseType(providerName);
        failure = null;

        if (providerName == null || provider != DatabaseType.Unknown)
            return true;

        failure = $"Couldn't find provider '{providerName}'.";
        return false;
    }

    private static IEnumerable<DataLinqDatabaseConfig> FilterDatabases(
        IEnumerable<DataLinqDatabaseConfig> databases,
        string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            return databases;

        return databases.Where(database => database.Name.Equals(databaseName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<DataLinqDatabaseConnection> FilterConnections(
        IEnumerable<DataLinqDatabaseConnection> connections,
        DatabaseType provider)
    {
        if (provider == DatabaseType.Unknown)
            return connections;

        return connections.Where(connection => connection.Type == provider);
    }
}
