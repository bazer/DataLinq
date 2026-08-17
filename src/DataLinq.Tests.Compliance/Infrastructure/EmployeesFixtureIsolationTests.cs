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
    private const string TriggerLeakProbe = "datalinq_fixture_trigger_leak_probe";

    [Test]
    [NotInParallel]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
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

    [Test]
    [NotInParallel]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ServerProviders))]
    public async Task IsolatedTinyFixture_RebuildsTriggerContaminationBeforeNextOwner(
        TestProviderDescriptor provider)
    {
        const int probeEmployeeNumber = 998766;
        string firstDatabaseName;

        using (var first = EmployeesTestDatabase.CreateIsolated(
                   provider,
                   "fixture-trigger-contamination-owner",
                   EmployeesFixtureProfile.TinySeeded))
        {
            firstDatabaseName = first.Connection.LogicalDatabaseName;
            using var connection = new MySqlConnection(first.Connection.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE TRIGGER `{TriggerLeakProbe}` BEFORE INSERT ON `employees` " +
                "FOR EACH ROW SET NEW.`first_name` = 'trigger-contaminated';";
            command.ExecuteNonQuery();

            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.TRIGGERS " +
                "WHERE TRIGGER_SCHEMA = DATABASE() AND TRIGGER_NAME = @triggerName;";
            command.Parameters.AddWithValue("@triggerName", TriggerLeakProbe);
            await Assert.That(Convert.ToInt32(command.ExecuteScalar())).IsEqualTo(1);
        }

        using var second = EmployeesTestDatabase.CreateIsolated(
            provider,
            "fixture-trigger-recovery-owner",
            EmployeesFixtureProfile.TinySeeded);

        await Assert.That(second.Connection.LogicalDatabaseName).IsEqualTo(firstDatabaseName);

        using (var connection = new MySqlConnection(second.Connection.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.TRIGGERS " +
                "WHERE TRIGGER_SCHEMA = DATABASE() AND TRIGGER_NAME = @triggerName;";
            command.Parameters.AddWithValue("@triggerName", TriggerLeakProbe);
            await Assert.That(Convert.ToInt32(command.ExecuteScalar())).IsEqualTo(0);
        }

        var employee = new EmployeesTestData().NewEmployee(probeEmployeeNumber);
        employee.first_name = "Recovered";
        employee.last_name = "Fixture";
        second.Database.Insert(employee);

        await Assert.That(second.Database.Query().Employees.Single(x => x.emp_no == probeEmployeeNumber).first_name)
            .IsEqualTo("Recovered");
    }

    private static DbConnection CreateConnection(TestProviderDescriptor provider, string connectionString) =>
        provider.ServerTarget is null
            ? new SqliteConnection(connectionString)
            : new MySqlConnection(connectionString);
}
