using System;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using MySqlConnector;

namespace DataLinq.MariaDB.Extensions.DependencyInjection;

/// <summary>Construction settings for a MariaDB registration, captured once on first resolution.</summary>
public sealed class MariaDBRegistrationOptions : DataLinqProviderOptions;

/// <summary>Explicit MariaDB provider selection for DataLinq registrations.</summary>
public static class MariaDBRegistrationExtensions
{
    /// <summary>Selects MariaDB. Supply settings with Bind and/or Configure before resolving the registration.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, MariaDBRegistrationOptions> UseMariaDB<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration) where TDatabase : class, IDatabaseModel<TDatabase>
        => DataLinqProviderRegistrationBuilder<TDatabase, MariaDBRegistrationOptions>.Create(registration,
            () => new(), connectionString => _ = new MySqlConnectionStringBuilder(connectionString),
            (settings, logging) => new MariaDBDatabase<TDatabase>(settings.ConnectionString, settings.DatabaseName, logging, settings.Execution));

    /// <summary>Selects MariaDB with a literal connection string. No configuration service is required.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, MariaDBRegistrationOptions> UseMariaDB<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration, string connectionString) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return registration.UseMariaDB().Configure(options => options.ConnectionString = connectionString);
    }

    /// <summary>Selects MariaDB with a connection-string name resolved from IConfiguration on first activation.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, MariaDBRegistrationOptions> UseMariaDBConnectionString<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration, string connectionStringName) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);
        return registration.UseMariaDB().Configure(options => options.ConnectionStringName = connectionStringName);
    }
}
