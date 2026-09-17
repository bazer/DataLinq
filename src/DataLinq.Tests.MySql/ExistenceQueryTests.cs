using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed class ExistenceQueryTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task TableNamesAreLiteralAndQueriesRemainParameterized(TestProviderDescriptor descriptor)
    {
        string[] names = ["literal_name", "literal%name", "quote'name", "tick`name", "雪", "CaseOnly", "accenté", "patternXtable", "prefixSuffix"];
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(TableNamesAreLiteralAndQueriesRemainParameterized),
            names.Select(name => $"CREATE TABLE {Quote(name)} (id INT PRIMARY KEY)").ToArray());
        using var logger = new CommandLoggerFactory();
        using var provider = CreateProvider(descriptor, schema, new DataLinqLoggingConfiguration(logger));
        foreach (var name in names)
            await Assert.That(provider.TableExists(name)).IsTrue();
        foreach (var name in new[] { "pattern_table", "prefix%", "absent' OR '1'='1", "absent`; SELECT 1; --", "accente", "missing" })
            await Assert.That(provider.TableExists(name)).IsFalse();

        var lowerCaseNames = Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT @@lower_case_table_names"));
        await Assert.That(provider.TableExists("caseonly")).IsEqualTo(lowerCaseNames != 0);
        await Assert.That(provider.TableExists("literal_name", schema.Connection.DataSourceName)).IsTrue();
        await Assert.That(provider.TableExists("literal_name", "absent'`_%schema")).IsFalse();
        await Assert.That(provider.TableExists("SCHEMATA", "INFORMATION_SCHEMA")).IsTrue();
        await Assert.That(provider.TableExists("schemata", "information_schema")).IsTrue();
        await Assert.That(() => provider.TableExists("")).Throws<ArgumentNullException>();

        var queries = logger.Messages.Where(message => message.StartsWith("SELECT 1 FROM information_schema.TABLES", StringComparison.Ordinal)).ToArray();
        await Assert.That(queries.Length).IsGreaterThan(0);
        foreach (var query in queries)
        {
            var sql = query.Split('\n')[0];
            await Assert.That(sql.Contains("@databaseName", StringComparison.Ordinal) && sql.Contains("@tableName", StringComparison.Ordinal)).IsTrue();
            await Assert.That(sql.Contains("LIKE", StringComparison.Ordinal)).IsFalse();
            await Assert.That(query.Contains("Parameters:", StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task DatabaseNamesAreLiteralIncludingQuotesDelimitersAndWildcards(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(DatabaseNamesAreLiteralIncludingQuotesDelimitersAndWildcards));
        using var provider = CreateProvider(descriptor, schema, DataLinqLoggingConfiguration.NullConfiguration);
        var prefix = "exist_" + Guid.NewGuid().ToString("N");
        var exactName = prefix + "_%'`雪";
        var patternTarget = prefix + "Xother";
        using var admin = new MySqlConnection(PodmanTestEnvironmentSettings.FromEnvironment().CreateAdminConnectionString(descriptor.ServerTarget!));
        admin.Open();
        var created = new List<string>();
        try
        {
            foreach (var name in new[] { exactName, patternTarget, prefix + "Case" })
            {
                using var create = new MySqlCommand($"CREATE DATABASE {Quote(name)}", admin);
                create.ExecuteNonQuery();
                created.Add(name);
            }
            await Assert.That(provider.DatabaseExists()).IsTrue();
            await Assert.That(provider.DatabaseExists(exactName)).IsTrue();
            await Assert.That(provider.DatabaseExists(prefix + "_other")).IsFalse();
            await Assert.That(provider.DatabaseExists(prefix + "%")).IsFalse();
            await Assert.That(provider.DatabaseExists(prefix + "' OR '1'='1")).IsFalse();
            await Assert.That(provider.DatabaseExists("INFORMATION_SCHEMA")).IsTrue();
            var lowerCaseNames = Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT @@lower_case_table_names"));
            await Assert.That(provider.DatabaseExists(prefix + "case")).IsEqualTo(lowerCaseNames != 0);
        }
        finally
        {
            foreach (var name in created)
            {
                using var drop = new MySqlCommand($"DROP DATABASE {Quote(name)}", admin);
                drop.ExecuteNonQuery();
            }
        }
    }

    private static SqlProvider<EmployeesDb> CreateProvider(TestProviderDescriptor descriptor, ServerSchemaDatabase schema, DataLinqLoggingConfiguration logging)
        => descriptor.DatabaseType == DatabaseType.MySQL
            ? new MySqlProvider<EmployeesDb>(schema.Connection.ConnectionString, schema.Connection.DataSourceName, logging)
            : new MariaDBProvider<EmployeesDb>(schema.Connection.ConnectionString, schema.Connection.DataSourceName, logging);

    private static string Quote(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    private sealed class CommandLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => categoryName == "DataLinq.SqlCommand" ? this : NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
