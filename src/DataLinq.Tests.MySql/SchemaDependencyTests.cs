using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class SchemaDependencyTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ImportedCycles_ReturnDiagnosticWithBothRestrictionSettings(TestProviderDescriptor provider)
    {
        foreach (var count in new[] { 2, 3 })
        {
            using var source = ServerSchemaDatabase.Create(provider, nameof(ImportedCycles_ReturnDiagnosticWithBothRestrictionSettings));
            for (var i = 0; i < count; i++) source.ExecuteNonQuery($"CREATE TABLE node{i} (id INT PRIMARY KEY, next_id INT NULL)");
            for (var i = 0; i < count; i++) source.ExecuteNonQuery($"ALTER TABLE node{i} ADD CONSTRAINT FK_Node{i} FOREIGN KEY (next_id) REFERENCES node{(i + 1) % count}(id)");
            var metadata = source.ParseDatabase("DependenciesDb", "DependenciesDb", "SchemaDependencies");
            await Assert.That(metadata.TableModels.Length).IsEqualTo(count);
            foreach (var restrict in new[] { true, false })
            {
                var result = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType).GetCreateTables(metadata, restrict);
                await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
                await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.NotImplemented);
                for (var i = 0; i < count; i++) await Assert.That(failure.Message).Contains($"node{i}");
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task SelfReferencesAndAcyclicDependencies_CreateUsableServerSchema(TestProviderDescriptor provider)
    {
        foreach (var self in new[] { true, false })
        {
            using var source = ServerSchemaDatabase.Create(provider, nameof(SelfReferencesAndAcyclicDependencies_CreateUsableServerSchema),
                self ? "CREATE TABLE node0 (id INT PRIMARY KEY, next_id INT NULL, FOREIGN KEY(next_id) REFERENCES node0(id))"
                    : "CREATE TABLE node1 (id INT PRIMARY KEY); CREATE TABLE node0 (id INT PRIMARY KEY, next_id INT NULL, FOREIGN KEY(next_id) REFERENCES node1(id))");
            var metadata = source.ParseDatabase("DependenciesDb", "DependenciesDb", "SchemaDependencies");
            var ddl = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType).GetCreateTables(metadata, true).ValueOrException().Text;
            using var destination = ServerSchemaDatabase.Create(provider, "GeneratedDependencies");
            destination.ExecuteNonQuery(ddl);
            if (!self) destination.ExecuteNonQuery("INSERT INTO node1 (id) VALUES (1)");
            destination.ExecuteNonQuery("INSERT INTO node0 (id) VALUES (1); UPDATE node0 SET next_id = 1");
            await Assert.That(() => destination.ExecuteNonQuery("UPDATE node0 SET next_id = 99")).Throws<MySqlException>();
            using var connection = new MySqlConnection(destination.Connection.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT next_id FROM node0 WHERE id = 1";
            await Assert.That(Convert.ToInt32(command.ExecuteScalar())).IsEqualTo(1);
        }
    }
}
