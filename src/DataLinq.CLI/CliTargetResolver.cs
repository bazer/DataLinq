using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Config;
using DataLinq.Metadata;

namespace DataLinq.CLI;

internal sealed record CliTargetFilter(
    string? DatabaseName,
    string? ProviderName);

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
        SecretResolutionContext? secrets = null)
    {
        if (!TryParseProviderFilter(filter.ProviderName, out var provider, out var providerFailure))
        {
            return new CliTargetExpansion(
                [],
                [new CliTargetExpansionFailure(configPath, providerFailure!)]);
        }

        var configPaths = recursive
            ? CliConfigDiscovery.DiscoverConfigFiles(configPath)
            : [CliConfigDiscovery.ResolveConfigFilePath(configPath)];

        if (recursive && configPaths.Count == 0)
        {
            return new CliTargetExpansion(
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

                foreach (var connection in FilterConnections(database.Connections, provider))
                    targets.Add(new CliConfigTarget(discoveredConfigPath, config, database, connection));
            }
        }

        return new CliTargetExpansion(targets, failures);
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
