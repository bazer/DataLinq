﻿using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.MySql;

/// <summary>
/// Factory for creating instances of MySQL database providers.
/// </summary>
public class MySqlDatabaseCreator : IDatabaseProviderCreator
{
    /// <summary>
    /// Determines if the provided type name corresponds to a MySQL or MariaDB database type.
    /// </summary>
    /// <param name="typeName">The name of the database type to check.</param>
    /// <returns>true if typeName is either 'mysql' or 'mariadb'; otherwise, false.</returns>
    public bool IsDatabaseType(string typeName)
    {
        return typeName.Equals("mysql", System.StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("mariadb", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a new MySqlDatabase provider for the specified type of database model.
    /// </summary>
    /// <typeparam name="T">The type of the database model.</typeparam>
    /// <param name="connectionString">The connection string for the database.</param>
    /// <param name="databaseName">The name of the database.</param>
    /// <returns>An instance of MySqlDatabase for the specified model type.</returns>
    Database<T> IDatabaseProviderCreator.GetDatabaseProvider<T>(string connectionString, string databaseName)
    {
        return new MySqlDatabase<T>(connectionString, databaseName);
    }
}

/// <summary>
/// Represents a MySQL database provider specific to a given database model type.
/// </summary>
/// <typeparam name="T">The type of the database model.</typeparam>
public class MySqlDatabase<T> : Database<T>
     where T : class, IDatabaseModel
{
    /// <summary>
    /// Initializes a new instance of the MySqlDatabase with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string for the MySQL database.</param>
    public MySqlDatabase(string connectionString) : base(new MySQLProvider<T>(connectionString))
    {
    }

    /// <summary>
    /// Initializes a new instance of the MySqlDatabase with the specified connection string and database name.
    /// </summary>
    /// <param name="connectionString">The connection string for the MySQL database.</param>
    /// <param name="databaseName">The name of the database.</param>
    public MySqlDatabase(string connectionString, string databaseName) : base(new MySQLProvider<T>(connectionString, databaseName))
    {
    }
}
