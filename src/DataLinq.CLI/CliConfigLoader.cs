using System;
using System.IO;
using System.Text.Json;
using DataLinq.Config;

namespace DataLinq.CLI;

internal static class CliConfigLoader
{
    public static bool TryRead(
        string configPath,
        Action<string> log,
        out DataLinqConfig config,
        out object failure,
        SecretResolutionContext? secrets = null)
    {
        try
        {
            if (secrets != null)
                return TryReadWithSecrets(configPath, log, secrets, out config, out failure);

            var read = DataLinqConfig.FindAndReadConfigs(configPath, log);
            if (read.HasFailed)
            {
                config = null!;
                failure = read.Failure;
                return false;
            }

            config = read.Value;
            failure = null!;
            return true;
        }
        catch (JsonException exception)
        {
            config = null!;
            failure = $"Couldn't parse config file '{configPath}'. {exception.Message}";
            return false;
        }
        catch (IOException exception)
        {
            config = null!;
            failure = $"Couldn't read config file '{configPath}'. {exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            config = null!;
            failure = $"Couldn't read config file '{configPath}'. {exception.Message}";
            return false;
        }
        catch (ArgumentException exception)
        {
            config = null!;
            failure = exception.Message;
            return false;
        }
    }

    private static bool TryReadWithSecrets(
        string configPath,
        Action<string> log,
        SecretResolutionContext secrets,
        out DataLinqConfig config,
        out object failure)
    {
        config = null!;
        failure = null!;

        var resolvedConfigPath = CliConfigDiscovery.ResolveConfigFilePath(configPath);
        if (!File.Exists(resolvedConfigPath))
        {
            failure = $"Couldn't find config file, usually called 'datalinq.json'. Tried searching path:\n{resolvedConfigPath}";
            return false;
        }

        log($"Reading config from:      {secrets.Redactor.Redact(resolvedConfigPath)}");
        var mainConfig = ConfigReader.Read(resolvedConfigPath);
        if (mainConfig == null)
        {
            failure = $"Couldn't parse config file {resolvedConfigPath}.";
            return false;
        }

        var configs = new System.Collections.Generic.List<ConfigFile>();
        var mainResolution = ResolveConfigSecrets(mainConfig, secrets);
        if (!mainResolution.Succeeded)
        {
            failure = mainResolution.Error!;
            return false;
        }

        configs.Add(mainConfig);

        var userFilePath = resolvedConfigPath.Replace(".json", ".user.json", StringComparison.OrdinalIgnoreCase);
        if (File.Exists(userFilePath))
        {
            log($"Reading user config from: {secrets.Redactor.Redact(userFilePath)}");
            var userConfig = ConfigReader.Read(userFilePath);
            if (userConfig == null)
            {
                failure = $"Couldn't parse config file {userFilePath}.";
                return false;
            }

            var userResolution = ResolveConfigSecrets(userConfig, secrets);
            if (!userResolution.Succeeded)
            {
                failure = userResolution.Error!;
                return false;
            }

            configs.Add(userConfig);
        }

        var basePath = Path.GetDirectoryName(resolvedConfigPath);
        if (basePath == null)
        {
            failure = $"Couldn't get directory name of path '{resolvedConfigPath}'";
            return false;
        }

        config = new DataLinqConfig(basePath, configs.ToArray());
        return true;
    }

    private static SecretResolutionResult ResolveConfigSecrets(ConfigFile config, SecretResolutionContext secrets)
    {
        foreach (var database in config.Databases)
        {
            var nameResult = ResolveNullableString(database.Name, secrets);
            if (!nameResult.Succeeded)
                return nameResult;
            database.Name = nameResult.Value;

            var csTypeResult = ResolveNullableString(database.CsType, secrets);
            if (!csTypeResult.Succeeded)
                return csTypeResult;
            database.CsType = csTypeResult.Value;

            var namespaceResult = ResolveNullableString(database.Namespace, secrets);
            if (!namespaceResult.Succeeded)
                return namespaceResult;
            database.Namespace = namespaceResult.Value;

            var modelDirectoryResult = ResolveNullableString(database.ModelDirectory, secrets);
            if (!modelDirectoryResult.Succeeded)
                return modelDirectoryResult;
            database.ModelDirectory = modelDirectoryResult.Value;

            var destinationDirectoryResult = ResolveNullableString(database.DestinationDirectory, secrets);
            if (!destinationDirectoryResult.Succeeded)
                return destinationDirectoryResult;
            database.DestinationDirectory = destinationDirectoryResult.Value;

            var fileEncodingResult = SecretReferenceResolver.ResolveString(database.FileEncoding, secrets);
            if (!fileEncodingResult.Succeeded)
                return fileEncodingResult;
            database.FileEncoding = fileEncodingResult.Value!;

            if (database.ModelLayout != null)
            {
                var propertyOrderResult = ResolveNullableString(database.ModelLayout.PropertyOrder, secrets);
                if (!propertyOrderResult.Succeeded)
                    return propertyOrderResult;
                database.ModelLayout.PropertyOrder = propertyOrderResult.Value;

                var primaryKeyPlacementResult = ResolveNullableString(database.ModelLayout.PrimaryKeyPlacement, secrets);
                if (!primaryKeyPlacementResult.Succeeded)
                    return primaryKeyPlacementResult;
                database.ModelLayout.PrimaryKeyPlacement = primaryKeyPlacementResult.Value;

                var foreignKeyPlacementResult = ResolveNullableString(database.ModelLayout.ForeignKeyPlacement, secrets);
                if (!foreignKeyPlacementResult.Succeeded)
                    return foreignKeyPlacementResult;
                database.ModelLayout.ForeignKeyPlacement = foreignKeyPlacementResult.Value;

                var relationPlacementResult = ResolveNullableString(database.ModelLayout.RelationPlacement, secrets);
                if (!relationPlacementResult.Succeeded)
                    return relationPlacementResult;
                database.ModelLayout.RelationPlacement = relationPlacementResult.Value;
            }

            if (database.SourceDirectories != null)
            {
                for (var i = 0; i < database.SourceDirectories.Count; i++)
                {
                    var result = SecretReferenceResolver.ResolveString(database.SourceDirectories[i], secrets);
                    if (!result.Succeeded)
                        return result;

                    database.SourceDirectories[i] = result.Value!;
                }
            }

            if (database.Include != null)
            {
                for (var i = 0; i < database.Include.Count; i++)
                {
                    var result = SecretReferenceResolver.ResolveString(database.Include[i], secrets);
                    if (!result.Succeeded)
                        return result;

                    database.Include[i] = result.Value!;
                }
            }

            foreach (var connection in database.Connections)
            {
                var typeResult = ResolveNullableString(connection.Type, secrets);
                if (!typeResult.Succeeded)
                    return typeResult;
                connection.Type = typeResult.Value;

                var databaseNameResult = ResolveNullableString(connection.DatabaseName, secrets);
                if (!databaseNameResult.Succeeded)
                    return databaseNameResult;
                connection.DatabaseName = databaseNameResult.Value;

                var dataSourceResult = ResolveNullableString(connection.DataSourceName, secrets);
                if (!dataSourceResult.Succeeded)
                    return dataSourceResult;
                connection.DataSourceName = dataSourceResult.Value;

                if (connection.ConnectionString != null)
                {
                    var connectionStringResult = SecretReferenceResolver.ResolveConnectionString(connection.ConnectionString, secrets);
                    if (!connectionStringResult.Succeeded)
                        return connectionStringResult;

                    connection.ConnectionString = connectionStringResult.Value;
                }
            }
        }

        return SecretResolutionResult.Success("true");
    }

    private static SecretResolutionResult ResolveNullableString(string? value, SecretResolutionContext secrets)
    {
        if (value == null)
            return SecretResolutionResult.Success(null!);

        return SecretReferenceResolver.ResolveString(value, secrets);
    }
}
