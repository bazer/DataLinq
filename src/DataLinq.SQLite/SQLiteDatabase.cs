using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace DataLinq.SQLite;

public class SQLiteDatabaseCreator : IDatabaseProviderCreator
{
    private ILoggerFactory? loggerFactory;

    public bool IsDatabaseType(string typeName)
    {
        return typeName.Equals("sqlite", System.StringComparison.OrdinalIgnoreCase);
    }

    Database<T> IDatabaseProviderCreator.GetDatabaseProvider<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicProperties)]
        T>(string connectionString, string databaseName)
    {
        return new SQLiteDatabase<T>(connectionString, databaseName, loggerFactory);
    }

    public SQLiteDatabaseCreator UseLoggerFactory(ILoggerFactory? loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        return this;
    }

    IDatabaseProviderCreator IDatabaseProviderCreator.UseLoggerFactory(ILoggerFactory? loggerFactory) =>
        UseLoggerFactory(loggerFactory);
}

public class SQLiteDatabase<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicProperties)]
    T> : Database<T>
     where T : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<T>
{
    public SQLiteDatabase(string connectionString, ILoggerFactory? loggerFactory = null) :
        this(connectionString, null, loggerFactory)
    { }

    public SQLiteDatabase(string connectionString, string? databaseName, ILoggerFactory? loggerFactory = null) :
        base(new SQLiteProvider<T>(
            connectionString,
            databaseName,
            loggerFactory is null ?
                DataLinqLoggingConfiguration.NullConfiguration :
                new DataLinqLoggingConfiguration(loggerFactory))) { }

    //public SQLiteDatabase(string connectionString, string databaseName) : base(new SQLiteProvider<T>(connectionString, databaseName))
    //{
    //}
}
