using System;
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DataLinq.Tests;

public class SQLiteInMemoryTests
{
    [Fact]
    public void AnonymousInMemoryDatabasesAreIsolatedPerDatabaseInstance()
    {
        const string connectionString = "Data Source=:memory:";
        const int empNo = 900001;

        using var firstDb = new SQLiteDatabase<EmployeesDb>(connectionString);
        using var secondDb = new SQLiteDatabase<EmployeesDb>(connectionString);

        var firstCreateResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            firstDb.Provider.Metadata,
            firstDb.Provider.DatabaseName,
            connectionString,
            true);
        var secondCreateResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            secondDb.Provider.Metadata,
            secondDb.Provider.DatabaseName,
            connectionString,
            true);

        if (firstCreateResult.HasFailed)
            Assert.Fail(firstCreateResult.Failure.ToString());

        if (secondCreateResult.HasFailed)
            Assert.Fail(secondCreateResult.Failure.ToString());

        Assert.NotEqual(firstDb.Provider.ConnectionString, secondDb.Provider.ConnectionString);

        var employee = new Helpers().NewEmployee(empNo);
        firstDb.Insert(employee);

        Assert.True(firstDb.Query().Employees.Any(x => x.emp_no == empNo));
        Assert.False(secondDb.Query().Employees.Any(x => x.emp_no == empNo));
    }

    [Fact]
    public void MetadataFactoryCanReadNamedInMemoryDatabaseSchema()
    {
        var databaseName = $"sqlite_metadata_{Guid.NewGuid():N}";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseName,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString;

        using var db = new SQLiteDatabase<EmployeesDb>(connectionString, databaseName);

        var createResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            db.Provider.Metadata,
            databaseName,
            connectionString,
            true);

        if (createResult.HasFailed)
            Assert.Fail(createResult.Failure.ToString());

        var factory = new MetadataFromSQLiteFactory(new MetadataFromDatabaseFactoryOptions());
        var metadata = factory.ParseDatabase(
            "employees",
            "Employees",
            "DataLinq.Tests.Models.Employees",
            databaseName,
            connectionString);

        Assert.True(metadata.HasValue, metadata.HasFailed ? metadata.Failure.ToString() : "Metadata parsing failed.");
        Assert.Contains(metadata.Value.TableModels, x => x.Table.DbName == "employees");
    }

    [Fact]
    public void FixtureInMemoryConnectionPointsToDatabaseWithSchema()
    {
        var connection = BaseTests.Fixture.EmployeeConnections.Single(x =>
            x.Type == DatabaseType.SQLite &&
            x.ConnectionString.Original.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase));

        using var sqliteConnection = new SqliteConnection(connection.ConnectionString.Original);
        sqliteConnection.Open();

        using var command = sqliteConnection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table', 'view') AND name <> 'sqlite_sequence'";

        var objectCount = Convert.ToInt32(command.ExecuteScalar());
        Assert.True(objectCount > 0, $"Expected schema objects in '{connection.ConnectionString.Original}', but found none.");
    }
}
