using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Tests.Compliance;

public sealed class EmployeesFixtureIsolationTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task IsolatedTinyFixture_RemovesRowSchemaSessionAndCacheStateBetweenOwners(
        TestProviderDescriptor provider)
    {
        const int probeEmployeeNumber = 998765;
        const string probeTable = "datalinq_fixture_leak_probe";
        string firstDatabaseName;
        string? baselineSessionSqlMode = null;
        long? baselineAutoIncrement = null;
        int baselineEmployeeCount;

        using (var first = EmployeesTestDatabase.CreateIsolated(
                   provider,
                   "fixture-contamination-owner",
                   EmployeesFixtureProfile.TinySeeded))
        {
            firstDatabaseName = first.Connection.LogicalDatabaseName;
            baselineEmployeeCount = first.Database.Query().Employees.Count();
            if (provider.ServerTarget is not null)
            {
                using var connection = new MySqlConnection(first.Connection.ConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT AUTO_INCREMENT FROM information_schema.TABLES " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'employees';";
                baselineAutoIncrement = Convert.ToInt64(command.ExecuteScalar());
                command.CommandText = "SELECT @@SESSION.sql_mode;";
                baselineSessionSqlMode = (string?)command.ExecuteScalar();
                command.CommandText = "SET SESSION sql_mode = 'ANSI';";
                command.ExecuteNonQuery();
            }

            var employee = new EmployeesTestData().NewEmployee(probeEmployeeNumber);
            employee.first_name = "Lease";
            employee.last_name = "Contamination";
            first.Database.Insert(employee);

            await Assert.That(first.Database.Query().Employees.Single(x => x.emp_no == probeEmployeeNumber).first_name)
                .IsEqualTo("Lease");
        }

        using (var second = EmployeesTestDatabase.CreateIsolated(
                   provider,
                   "fixture-next-owner",
                   EmployeesFixtureProfile.TinySeeded))
        {
            if (provider.ServerTarget is not null)
                await Assert.That(second.Connection.LogicalDatabaseName).IsEqualTo(firstDatabaseName);

            await Assert.That(second.Database.Query().Employees.Count()).IsEqualTo(baselineEmployeeCount);
            await Assert.That(second.Database.Query().Employees.Any(x => x.emp_no == probeEmployeeNumber)).IsFalse();

            using var connection = CreateConnection(provider, second.Connection.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            if (provider.ServerTarget is not null)
            {
                command.CommandText = "SELECT @@SESSION.sql_mode;";
                await Assert.That((string?)command.ExecuteScalar()).IsEqualTo(baselineSessionSqlMode);
                command.CommandText =
                    "SELECT AUTO_INCREMENT FROM information_schema.TABLES " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'employees';";
                await Assert.That(Convert.ToInt64(command.ExecuteScalar())).IsEqualTo(baselineAutoIncrement!.Value);
            }

            command.CommandText = provider.ServerTarget is null
                ? $"CREATE TABLE {probeTable} (id INTEGER PRIMARY KEY);"
                : $"CREATE TABLE `{probeTable}` (`id` INT NOT NULL PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        using var third = EmployeesTestDatabase.CreateIsolated(
            provider,
            "fixture-schema-recovery-owner",
            EmployeesFixtureProfile.TinySeeded);

        if (provider.ServerTarget is not null)
            await Assert.That(third.Connection.LogicalDatabaseName).IsEqualTo(firstDatabaseName);

        await Assert.That(third.Database.TableExists(probeTable)).IsFalse();
        await Assert.That(third.Database.Query().Employees.Count()).IsEqualTo(baselineEmployeeCount);
    }

    private static DbConnection CreateConnection(TestProviderDescriptor provider, string connectionString) =>
        provider.ServerTarget is null
            ? new SqliteConnection(connectionString)
            : new MySqlConnection(connectionString);
}
