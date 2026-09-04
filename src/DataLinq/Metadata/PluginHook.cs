using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;
using DataLinq.Query;
using Microsoft.Extensions.Logging;
using ThrowAway;

namespace DataLinq.Metadata;

public interface IDatabaseProviderCreator
{
    Database<T> GetDatabaseProvider<T>(string connectionString, string databaseName) where T : class, IDatabaseModel<T>;
    bool IsDatabaseType(string typeName);
    IDatabaseProviderCreator UseLoggerFactory(ILoggerFactory? loggerFactory);
}

public interface ISqlFromMetadataFactory
{
    Option<Sql, IDLOptionFailure> GetCreateTables(DatabaseDefinition metadata, bool foreignKeyRestrict);
    Option<int, IDLOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict);
}

public interface IMetadataFromDatabaseFactoryCreator
{
    IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options);
}

public interface IMetadataFromSqlFactory
{
    Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString);
}

/// <summary>The three services installed together for one SQL provider.</summary>
public sealed record DatabaseProviderRegistration(
    IDatabaseProviderCreator DatabaseProvider,
    ISqlFromMetadataFactory SqlFromMetadataFactory,
    IMetadataFromDatabaseFactoryCreator MetadataFromSqlFactory);

public static class PluginHook
{
    private static readonly object RegistrationGate = new();
    private static RegistrySnapshot snapshot = new();

    public static IReadOnlyDictionary<DatabaseType, DatabaseProviderRegistration> Registrations => Volatile.Read(ref snapshot).Registrations;
    public static IReadOnlyDictionary<DatabaseType, IDatabaseProviderCreator> DatabaseProviders => Volatile.Read(ref snapshot).DatabaseProviders;
    public static IReadOnlyDictionary<DatabaseType, ISqlFromMetadataFactory> SqlFromMetadataFactories => Volatile.Read(ref snapshot).SqlFromMetadataFactories;
    public static IReadOnlyDictionary<DatabaseType, IMetadataFromDatabaseFactoryCreator> MetadataFromSqlFactories => Volatile.Read(ref snapshot).MetadataFromSqlFactories;

    public static bool IsRegistered(DatabaseType type) => Registrations.ContainsKey(type);

    public static bool TryGetRegistration(DatabaseType type, [NotNullWhen(true)] out DatabaseProviderRegistration? registration) =>
        Registrations.TryGetValue(type, out registration);

    /// <summary>
    /// Atomically installs all services. Returns false if already registered, unless
    /// replaceExisting is explicitly enabled. Previously captured snapshots stay valid.
    /// </summary>
    public static bool RegisterProvider(
        DatabaseType type,
        IDatabaseProviderCreator databaseProvider,
        ISqlFromMetadataFactory sqlFromMetadataFactory,
        IMetadataFromDatabaseFactoryCreator metadataFromSqlFactory,
        bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(databaseProvider);
        ArgumentNullException.ThrowIfNull(sqlFromMetadataFactory);
        ArgumentNullException.ThrowIfNull(metadataFromSqlFactory);
        lock (RegistrationGate)
        {
            var current = snapshot;
            if (!replaceExisting && current.Registrations.ContainsKey(type))
                return false;

            var registration = new DatabaseProviderRegistration(databaseProvider, sqlFromMetadataFactory, metadataFromSqlFactory);
            Volatile.Write(ref snapshot, new RegistrySnapshot
            {
                Registrations = current.Registrations.SetItem(type, registration),
                DatabaseProviders = current.DatabaseProviders.SetItem(type, databaseProvider),
                SqlFromMetadataFactories = current.SqlFromMetadataFactories.SetItem(type, sqlFromMetadataFactory),
                MetadataFromSqlFactories = current.MetadataFromSqlFactories.SetItem(type, metadataFromSqlFactory)
            });
            return true;
        }
    }

    private sealed class RegistrySnapshot
    {
        internal ImmutableDictionary<DatabaseType, DatabaseProviderRegistration> Registrations { get; init; } = ImmutableDictionary<DatabaseType, DatabaseProviderRegistration>.Empty;
        internal ImmutableDictionary<DatabaseType, IDatabaseProviderCreator> DatabaseProviders { get; init; } = ImmutableDictionary<DatabaseType, IDatabaseProviderCreator>.Empty;
        internal ImmutableDictionary<DatabaseType, ISqlFromMetadataFactory> SqlFromMetadataFactories { get; init; } = ImmutableDictionary<DatabaseType, ISqlFromMetadataFactory>.Empty;
        internal ImmutableDictionary<DatabaseType, IMetadataFromDatabaseFactoryCreator> MetadataFromSqlFactories { get; init; } = ImmutableDictionary<DatabaseType, IMetadataFromDatabaseFactoryCreator>.Empty;
    }

    public static Option<int, IDLOptionFailure> CreateDatabaseFromSql(this DatabaseType type, Sql sql, string databaseOrFile, string connectionString, bool foreignKeyRestrict)
    {
        if (!TryGetRegistration(type, out var registration))
            return new DLOptionFailure<string>($"No creator for {type}");

        return registration.SqlFromMetadataFactory.CreateDatabase(sql, databaseOrFile, connectionString, foreignKeyRestrict);
    }

    public static Option<int, IDLOptionFailure> CreateDatabaseFromMetadata(this DatabaseType type, DatabaseDefinition metadata, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict)
    {
        if (!TryGetRegistration(type, out var registration))
            return new DLOptionFailure<string>($"No creator for {type}");

        var sql = registration.SqlFromMetadataFactory.GetCreateTables(metadata, foreignKeyRestrict);
        if (sql.HasFailed)
            return sql.Failure;
        return registration.SqlFromMetadataFactory.CreateDatabase(sql.Value, databaseNameOrFile, connectionString, foreignKeyRestrict);
    }

    public static Option<Sql, IDLOptionFailure> GenerateSql(this DatabaseType type, DatabaseDefinition metadata, bool foreignKeyRestrict)
    {
        if (!TryGetRegistration(type, out var registration))
            return new DLOptionFailure<string>($"No handler for {type}");

        return registration.SqlFromMetadataFactory.GetCreateTables(metadata, foreignKeyRestrict);
    }
}
