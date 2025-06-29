using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using Microsoft.Extensions.Logging;

namespace DataLinq.MariaDB;

public class MariaDBDatabaseCreator : IDatabaseProviderCreator
{
    private ILoggerFactory? loggerFactory;

    public bool IsDatabaseType(string typeName)
    {
        return typeName.Equals("mariadb", System.StringComparison.OrdinalIgnoreCase);
    }

    public Database<T> GetDatabaseProvider<T>(string connectionString, string databaseName) where T : class, IDatabaseModel
    {
        return new MariaDBDatabase<T>(connectionString, databaseName, loggerFactory);
    }

    public IDatabaseProviderCreator UseLoggerFactory(ILoggerFactory? loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        return this;
    }
}

public class MariaDBDatabase<T> : Database<T>
     where T : class, IDatabaseModel
{
    /// <summary>
    /// Initializes a new instance of the MySqlDatabase with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string for the MySQL database.</param>
    public MariaDBDatabase(string connectionString) : base(new MariaDBProvider<T>(connectionString))
    {
    }

    /// <summary>
    /// Initializes a new instance of the MySqlDatabase with the specified connection string and logger factory.
    /// </summary>
    /// <param name="connectionString">The connection string for the MySQL database.</param>
    /// <param name="loggerFactory">The logger factory to use for logging.</param>
    public MariaDBDatabase(string connectionString, ILoggerFactory? loggerFactory) : base(new MariaDBProvider<T>(connectionString, null, loggerFactory == null ? DataLinqLoggingConfiguration.NullConfiguration : new DataLinqLoggingConfiguration(loggerFactory)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the MySqlDatabase with the specified connection string and database name.
    /// </summary>
    /// <param name="connectionString">The connection string for the MySQL database.</param>
    /// <param name="databaseName">The name of the database.</param>
    public MariaDBDatabase(string connectionString, string databaseName) : base(new MariaDBProvider<T>(connectionString, databaseName))
    {
    }

    /// <summary>
    /// Initializes a new instance of the MySqlDatabase with the specified connection string, database name and logger factory.
    /// </summary>
    /// <param name="connectionString">The connection string for the MySQL database.</param>
    /// <param name="databaseName">The name of the database.</param>
    /// /// <param name="loggerFactory">The logger factory to use for logging.</param>
    public MariaDBDatabase(string connectionString, string databaseName, ILoggerFactory? loggerFactory) : base(new MariaDBProvider<T>(connectionString, databaseName, loggerFactory == null ? DataLinqLoggingConfiguration.NullConfiguration : new DataLinqLoggingConfiguration(loggerFactory)))
    {
    }
}