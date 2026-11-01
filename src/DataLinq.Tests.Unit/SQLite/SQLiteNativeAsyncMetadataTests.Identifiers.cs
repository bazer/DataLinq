using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Metadata;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public sealed partial class SQLiteNativeAsyncMetadataTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeIdentifiers_CanonicalReferencesPreserveNonAsciiTablesAndColumns(bool asynchronous)
    {
        using var fixture = new Fixture(true, """
            CREATE TABLE "åparent"("åid" INTEGER PRIMARY KEY, "Åid" INTEGER NOT NULL UNIQUE);
            CREATE TABLE "Åparent"("åid" INTEGER PRIMARY KEY);
            CREATE TABLE child(id INTEGER PRIMARY KEY, first INTEGER, second INTEGER, third INTEGER,
                FOREIGN KEY(first) REFERENCES "åPARENT"("åID"),
                FOREIGN KEY(second) REFERENCES "åPARENT"("ÅID"),
                FOREIGN KEY(third) REFERENCES "ÅPARENT"("åID"));
            CREATE INDEX "åindex" ON "åparent"("åid");
            CREATE INDEX "Åindex" ON "åparent"("Åid");
            """);
        var settings = MetadataReadSettings.Runtime(null, null);
        var live = (asynchronous ? await fixture.Provider.ReadValidationMetadataAsyncCore(settings)
            : fixture.Provider.ReadValidationMetadataCore(settings)).ValueOrException();
        var child = live.TableModels.Single(table => table.Table.DbName == "child");
        var targets = child.Table.ColumnIndices.SelectMany(index => index.RelationParts)
            .Where(part => part.Type == RelationPartType.ForeignKey)
            .Select(part => part.Relation.CandidateKey.ColumnIndex)
            .Select(index => index.Table.DbName + "." + index.Columns.Single().DbName).ToArray();
        await Assert.That(targets).IsEquivalentTo(["åparent.åid", "åparent.Åid", "Åparent.åid"]);
        var evidence = settings.Limitations!.Capture(settings.Identifiers!, live.TableModels.Select(table => table.Table.DbName));
        await Assert.That(evidence.Issues).IsEmpty();
        var selected = SchemaComparisonScope.Capture(live, DatabaseType.SQLite, ["CHILD"]).Resolve(live, settings.Identifiers!);
        await Assert.That(new SchemaComparer(settings.Identifiers!).CompareRuntime(selected.Model, selected.Database)).IsEmpty();
    }
}
