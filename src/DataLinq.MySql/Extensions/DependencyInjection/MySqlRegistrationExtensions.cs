using System;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using MySqlConnector;

namespace DataLinq.MySql.Extensions.DependencyInjection;

/// <summary>Construction settings for a MySql registration, captured once on first resolution.</summary>
public sealed class MySqlRegistrationOptions : DataLinqProviderOptions;

/// <summary>Explicit MySql provider selection for DataLinq registrations.</summary>
public static class MySqlRegistrationExtensions
{
    /// <summary>Selects MySql. Supply settings with Bind and/or Configure before resolving the registration.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, MySqlRegistrationOptions> UseMySql<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration) where TDatabase : class, IDatabaseModel<TDatabase>
        => DataLinqProviderRegistrationBuilder<TDatabase, MySqlRegistrationOptions>.Create(registration,
            () => new(), connectionString => _ = new MySqlConnectionStringBuilder(connectionString),
            (settings, logging) => new MySqlDatabase<TDatabase>(settings.ConnectionString, settings.DatabaseName, logging, settings.Execution));

    /// <summary>Selects MySql with a literal connection string. No configuration service is required.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, MySqlRegistrationOptions> UseMySql<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration, string connectionString) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return registration.UseMySql().Configure(options => options.ConnectionString = connectionString);
    }

    /// <summary>Selects MySql with a connection-string name resolved from IConfiguration on first activation.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, MySqlRegistrationOptions> UseMySqlConnectionString<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration, string connectionStringName) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);
        return registration.UseMySql().Configure(options => options.ConnectionStringName = connectionStringName);
    }
}
