using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using DataLinq.Testing;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed partial class NativeAsyncMetadataTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RuntimeLimitations_ServerCatalogueDoesNotDependOnLogging(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(RuntimeLimitations_ServerCatalogueDoesNotDependOnLogging),
            "SET SESSION sql_mode=''",
            $"""
            CREATE TABLE observed(
                id INT PRIMARY KEY, value INT NOT NULL DEFAULT 1, label VARCHAR(40) NOT NULL,
                zero_date DATE DEFAULT '0000-00-00', calculated INT GENERATED ALWAYS AS (value + 1) VIRTUAL,
                INDEX ix_prefix(label(4)), INDEX ix_descending(value DESC), INDEX ix_generated(calculated),
                INDEX ix_hidden(value) {(descriptor.DatabaseType == DatabaseType.MySQL ? "INVISIBLE" : "IGNORED")}
            )
            """);
        if (descriptor.DatabaseType == DatabaseType.MySQL)
            schema.ExecuteNonQuery("SET SESSION sql_mode=''; CREATE INDEX ix_expression ON observed((value + 2))");
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        string[]? messages = null;
        string? digest = null;
        foreach (var asynchronous in new[] { false, true })
        foreach (var logging in new[] { false, true })
        {
            var logs = new List<string>();
            var settings = MetadataReadSettings.Runtime(null, logging ? logs.Add : null);
            var live = (asynchronous ? await provider.ReadValidationMetadataAsyncCore(settings) : provider.ReadValidationMetadataCore(settings)).ValueOrException();
            var evidence = settings.Limitations!.Capture(settings.Identifiers!, ["observed"]);
            var current = evidence.Issues.Select(issue => issue.Message).ToArray();
            await Assert.That(evidence.Issues.Count).IsEqualTo(descriptor.DatabaseType == DatabaseType.MySQL ? 7 : 6);
            await Assert.That(evidence.IsColumnOmitted("observed", "calculated")).IsTrue();
            await Assert.That(evidence.IsDefaultOmitted("observed", "zero_date")).IsTrue();
            foreach (var name in new[] { "ix_prefix", "ix_descending", "ix_generated", "ix_hidden" })
                await Assert.That(evidence.IsIndexOmitted("observed", name)).IsTrue();
            if (descriptor.DatabaseType == DatabaseType.MySQL) await Assert.That(evidence.IsIndexOmitted("observed", "ix_expression")).IsTrue();
            if (logging) await Assert.That(current.OrderBy(message => message).SequenceEqual(logs.OrderBy(message => message))).IsTrue();
            else await Assert.That(logs).IsEmpty();
            if (messages is not null) await Assert.That(current.SequenceEqual(messages)).IsTrue();
            if (digest is not null) await Assert.That(MetadataEquivalenceDigest.CreateText(live)).IsEqualTo(digest);
            messages = current;
            digest = MetadataEquivalenceDigest.CreateText(live);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RuntimeLimitations_ExternalForeignKeyCannotBecomeALocalRelationship(TestProviderDescriptor descriptor)
    {
        using var external = ServerSchemaDatabase.Create(descriptor, nameof(RuntimeLimitations_ExternalForeignKeyCannotBecomeALocalRelationship) + "_external",
            "CREATE TABLE parent(id INT PRIMARY KEY)");
        using var local = ServerSchemaDatabase.Create(descriptor, nameof(RuntimeLimitations_ExternalForeignKeyCannotBecomeALocalRelationship),
            "CREATE TABLE parent(id INT PRIMARY KEY)",
            "CREATE TABLE child(id INT PRIMARY KEY, parent_id INT, CONSTRAINT fk_parent FOREIGN KEY(parent_id) REFERENCES parent(id))");
        using var provider = Provider(descriptor, local.Connection.ConnectionString, local.Connection.DataSourceName);
        var model = provider.ReadValidationMetadataCore().ValueOrException();
        local.ExecuteNonQuery($"ALTER TABLE child DROP FOREIGN KEY fk_parent; ALTER TABLE child ADD CONSTRAINT fk_parent FOREIGN KEY(parent_id) REFERENCES {SqlIdentifier.Quote(external.Connection.DataSourceName, "`")}.parent(id)");
        foreach (var asynchronous in new[] { false, true })
        {
            var settings = MetadataReadSettings.Runtime(null, null);
            var live = (asynchronous ? await provider.ReadValidationMetadataAsyncCore(settings) : provider.ReadValidationMetadataCore(settings)).ValueOrException();
            var evidence = settings.Limitations!.Capture(settings.Identifiers!, ["child"]);
            await Assert.That(evidence.Issues.Count).IsEqualTo(1);
            await Assert.That(evidence.Issues.Single().Message).Contains(external.Connection.DataSourceName + ".parent.id");
            var scope = SchemaComparisonScope.Capture(model, descriptor.DatabaseType, ["child"]).Resolve(live, settings.Identifiers!);
            var differences = new SchemaComparer(settings.Identifiers!, evidence).CompareRuntime(scope.Model, scope.Database);
            // The expected local endpoint is provably absent, despite the same
            // constraint and table spellings appearing in the other database.
            await Assert.That(differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingForeignKey);
        }
    }
}
