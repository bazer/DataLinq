using System;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite.Extensions.DependencyInjection;

/// <summary>Construction settings for a SQLite registration, captured once on first resolution.</summary>
public sealed class SQLiteRegistrationOptions : DataLinqProviderOptions;

/// <summary>Explicit SQLite provider selection for DataLinq registrations.</summary>
public static class SQLiteRegistrationExtensions
{
    /// <summary>Selects SQLite. Supply settings with Bind and/or Configure before resolving the registration.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, SQLiteRegistrationOptions> UseSQLite<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration) where TDatabase : class, IDatabaseModel<TDatabase>
        => DataLinqProviderRegistrationBuilder<TDatabase, SQLiteRegistrationOptions>.Create(registration,
            () => new(), connectionString => _ = new SqliteConnectionStringBuilder(connectionString),
            (settings, logging) => new SQLiteDatabase<TDatabase>(settings.ConnectionString, settings.DatabaseName, logging, settings.Execution));

    /// <summary>Selects SQLite with a literal connection string. No configuration service is required.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, SQLiteRegistrationOptions> UseSQLite<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration, string connectionString) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return registration.UseSQLite().Configure(options => options.ConnectionString = connectionString);
    }

    /// <summary>Selects SQLite with a connection-string name resolved from IConfiguration on first activation.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, SQLiteRegistrationOptions> UseSQLiteConnectionString<TDatabase>(
        this DataLinqRegistrationBuilder<TDatabase> registration, string connectionStringName) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);
        return registration.UseSQLite().Configure(options => options.ConnectionStringName = connectionStringName);
    }
}
