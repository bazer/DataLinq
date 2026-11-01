using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Testing;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public sealed partial class SQLiteNativeAsyncMetadataTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeLimitations_SelectedRelationshipKeepsItsUnrepresentedEndpointIssue(bool omittedSource)
    {
        using var fixture = new Fixture(true, """
            CREATE TABLE parent(id INTEGER PRIMARY KEY);
            CREATE TABLE child(id INTEGER PRIMARY KEY, parent_id INTEGER, FOREIGN KEY(parent_id) REFERENCES parent(id));
            """);
        var model = fixture.Provider.ReadValidationMetadataCore().ValueOrException();
        fixture.Provider.DatabaseAccess.ExecuteNonQuery(omittedSource
            ? "DROP TABLE child; CREATE TABLE child(id INTEGER PRIMARY KEY, parent_id INTEGER GENERATED ALWAYS AS(id + 1) STORED, FOREIGN KEY(parent_id) REFERENCES parent(id))"
            : "DROP TABLE parent");
        foreach (var asynchronous in new[] { false, true })
        {
            var settings = MetadataReadSettings.Runtime(null, null);
            var live = (asynchronous ? await fixture.Provider.ReadValidationMetadataAsyncCore(settings) : fixture.Provider.ReadValidationMetadataCore(settings)).ValueOrException();
            var scope = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, ["child"]).Resolve(live, settings.Identifiers!);
            var evidence = settings.Limitations!.Capture(settings.Identifiers!, ["child"]);
            await Assert.That(evidence.Issues.Count).IsEqualTo(omittedSource ? 2 : 1);
            await Assert.That(scope.Model.Count).IsEqualTo(1);
            await Assert.That(scope.Database.Count).IsEqualTo(1);
            await Assert.That(new SchemaComparer(settings.Identifiers!, evidence).CompareRuntime(scope.Model, scope.Database)).IsEmpty();
            var full = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, null).Resolve(live, settings.Identifiers!);
            var differences = new SchemaComparer(settings.Identifiers!, evidence).CompareRuntime(full.Model, full.Database);
            if (omittedSource) await Assert.That(differences).IsEmpty();
            else await Assert.That(differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingTable);
        }
    }

    [Test]
    public async Task RuntimeLimitations_AutomaticIndexRetainsObservedNameAndComparisonIdentity()
    {
        using var fixture = new Fixture(true, """
            CREATE TABLE observed(id INTEGER PRIMARY KEY, calculated INTEGER GENERATED ALWAYS AS(id + 1) STORED, UNIQUE(calculated DESC))
            """);
        var settings = MetadataReadSettings.Runtime(null, null);
        _ = fixture.Provider.ReadValidationMetadataCore(settings).ValueOrException();
        var evidence = settings.Limitations!.Capture(settings.Identifiers!, ["observed"]);
        await Assert.That(evidence.Issues.Count).IsEqualTo(2);
        await Assert.That(evidence.IsIndexOmitted("observed", "calculated")).IsTrue();
        await Assert.That(evidence.Issues.Any(issue => issue.ObjectPath == "observed.sqlite_autoindex_observed_1")).IsTrue();
    }

    [Test]
    public async Task RuntimeLimitations_HiddenDetectionDoesNotDowngradeExistingUnsupportedSchemaFailure()
    {
        using var fixture = new Fixture(true, "CREATE VIRTUAL TABLE hidden_source USING fts5(content)");
        foreach (var asynchronous in new[] { false, true })
        {
            var settings = MetadataReadSettings.Runtime(null, null);
            var result = asynchronous ? await fixture.Provider.ReadValidationMetadataAsyncCore(settings) : fixture.Provider.ReadValidationMetadataCore(settings);
            await Assert.That(result.TryUnwrap(out _, out _)).IsFalse();
            // Detection takes place, but an incomplete/unsupported metadata
            // graph still cannot be published as a completed validation result.
            var evidence = settings.Limitations!.Capture(settings.Identifiers!, ["hidden_source"]);
            await Assert.That(evidence.IsColumnOmitted("hidden_source", "rank")).IsTrue();
            await Assert.That(evidence.IsColumnOmitted("hidden_source", "hidden_source")).IsTrue();
        }
    }

    [Test]
    public async Task RuntimeLimitations_PartialCompositeForeignKeyCannotPublishReducedShape()
    {
        using var fixture = new Fixture(true, """
            CREATE TABLE parent(first INTEGER, second INTEGER, PRIMARY KEY(first, second), UNIQUE(second));
            CREATE TABLE child(id INTEGER PRIMARY KEY, first INTEGER GENERATED ALWAYS AS(id + 1) STORED, second INTEGER,
                FOREIGN KEY(first,second) REFERENCES parent(first,second));
            """);
        foreach (var asynchronous in new[] { false, true })
        {
            var result = asynchronous ? await fixture.Provider.ReadValidationMetadataAsyncCore() : fixture.Provider.ReadValidationMetadataCore();
            await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
            await Assert.That(failure.Message).Contains("partially representable");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeLimitations_DetectsGeneratedColumnsIndexesAndDefaultsWithoutLogging(bool memory)
    {
        using var fixture = new Fixture(memory, """
            CREATE TABLE observed(
                id INTEGER PRIMARY KEY,
                value INTEGER DEFAULT (1 + 2),
                payload BLOB DEFAULT X'0123',
                calculated INTEGER GENERATED ALWAYS AS (id + 1) VIRTUAL,
                stored INTEGER GENERATED ALWAYS AS (id + 2) STORED
            );
            CREATE INDEX ix_partial ON observed(value) WHERE value > 0;
            CREATE INDEX ix_expression ON observed((value + 1));
            CREATE INDEX ix_descending ON observed(value DESC);
            CREATE INDEX ix_generated ON observed(calculated);
            CREATE TRIGGER ignored_trigger AFTER INSERT ON observed BEGIN SELECT 1; END;
            """);
        string[]? messages = null;
        string? digest = null;
        foreach (var asynchronous in new[] { false, true })
        foreach (var logging in new[] { false, true })
        {
            var logs = new List<string>();
            var settings = MetadataReadSettings.Runtime(null, logging ? logs.Add : null);
            var live = (asynchronous
                ? await fixture.Provider.ReadValidationMetadataAsyncCore(settings)
                : fixture.Provider.ReadValidationMetadataCore(settings)).ValueOrException();
            var evidence = settings.Limitations!.Capture(settings.Identifiers!, live.TableModels.Select(table => table.Table.DbName));
            var current = evidence.Issues.Select(issue => issue.Message).ToArray();
            await Assert.That(evidence.Issues.Count).IsEqualTo(8);
            await Assert.That(live.TableModels.Length).IsEqualTo(1);
            await Assert.That(live.TableModels.Single().Table.Columns.Select(column => column.DbName).ToArray()).IsEquivalentTo(["id", "value", "payload"]);
            await Assert.That(evidence.IsColumnOmitted("observed", "calculated")).IsTrue();
            await Assert.That(evidence.IsColumnOmitted("observed", "stored")).IsTrue();
            await Assert.That(evidence.IsDefaultOmitted("observed", "value")).IsTrue();
            await Assert.That(evidence.IsDefaultOmitted("observed", "payload")).IsTrue();
            foreach (var index in new[] { "ix_partial", "ix_expression", "ix_descending", "ix_generated" })
                await Assert.That(evidence.IsIndexOmitted("observed", index)).IsTrue();
            if (logging) await Assert.That(current.OrderBy(message => message).SequenceEqual(logs.OrderBy(message => message))).IsTrue();
            else await Assert.That(logs).IsEmpty();
            if (messages is not null) await Assert.That(current.SequenceEqual(messages)).IsTrue();
            if (digest is not null) await Assert.That(MetadataEquivalenceDigest.CreateText(live)).IsEqualTo(digest);
            messages = current;
            digest = MetadataEquivalenceDigest.CreateText(live);
        }
    }
}
