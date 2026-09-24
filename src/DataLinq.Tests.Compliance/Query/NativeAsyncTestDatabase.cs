using System;
using DataLinq.Interfaces;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DataLinq.Tests.Compliance;

// The standard scope provisions and removes the isolated schema. Execution
// explicitly enables pooling instead of inheriting the setup fixture's false
// setting. Microsoft.Data.Sqlite itself excludes memory mode from pooling.
// No global provider registration/logging options are changed.
internal sealed class NativeAsyncTestDatabase<T> : IDisposable where T : class, IDatabaseModel<T>
{
    private readonly TemporaryModelTestDatabase<T> setup;
    internal Database<T> Database { get; }

    internal NativeAsyncTestDatabase(TestProviderDescriptor descriptor, string scenario, ILoggerFactory? logger = null)
    {
        setup = TemporaryModelTestDatabase<T>.Create(descriptor, scenario);
        try
        {
            var connection = setup.Connection;
            Database = descriptor.DatabaseType switch
            {
                DatabaseType.SQLite => new SQLiteDatabase<T>(new SqliteConnectionStringBuilder(connection.ConnectionString)
                    { Pooling = true }.ConnectionString, connection.DataSourceName, logger),
                DatabaseType.MySQL => new MySqlDatabase<T>(new MySqlConnectionStringBuilder(connection.ConnectionString)
                    { Pooling = true }.ConnectionString, connection.DataSourceName, logger),
                DatabaseType.MariaDB => new MariaDBDatabase<T>(new MySqlConnectionStringBuilder(connection.ConnectionString)
                    { Pooling = true }.ConnectionString, connection.DataSourceName, logger),
                _ => throw new NotSupportedException("This native fixture requires a SQL provider.")
            };
        }
        catch { setup.Dispose(); throw; }
    }

    public void Dispose()
    {
        try { Database.Dispose(); }
        finally
        {
            try
            {
                if (Database.DatabaseType == DatabaseType.SQLite)
                {
                    using var pool = new SqliteConnection(Database.Provider.ConnectionString);
                    SqliteConnection.ClearPool(pool);
                }
            }
            finally { setup.Dispose(); }
        }
    }
}
