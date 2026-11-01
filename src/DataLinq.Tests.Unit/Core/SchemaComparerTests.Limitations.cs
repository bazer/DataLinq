using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public partial class SchemaComparerTests
{
    [Test]
    public async Task RuntimeLimitations_WithholdOnlyTheSpecificUnprovableClaim()
    {
        var model = CreateDatabase(CreateTable("observed", [
            CreateColumn("id", typeof(int), false, primaryKey: true),
            CreateColumn("generated", typeof(int), false),
            CreateColumn("genuinely_missing", typeof(int), false),
            CreateColumn("value", typeof(int), false, defaultValue: 42)], [
            new IndexAttribute("ix_partial", IndexCharacteristic.Simple, IndexType.BTREE, "id"),
            new IndexAttribute("ix_absent", IndexCharacteristic.Simple, IndexType.BTREE, "id")]));
        var live = CreateDatabase(CreateTable("observed", [
            CreateColumn("id", typeof(int), false, primaryKey: true), CreateColumn("value", typeof(int), true)]));
        var limitations = new SchemaReadLimitations();
        limitations.Add(new("observed", "generated", SchemaReadAspect.Column, SchemaReadLimitationReason.GeneratedColumn, "Generated column was observed."));
        limitations.Add(new("observed", "value", SchemaReadAspect.ColumnDefault, SchemaReadLimitationReason.UnsupportedDefault, "Default could not be represented."));
        limitations.Add(new("observed", "ix_partial", SchemaReadAspect.Index, SchemaReadLimitationReason.PartialIndex, "Partial index was observed."));
        var evidence = limitations.Capture(SchemaIdentifierRules.SQLite, ["observed"]);
        var selected = SchemaComparisonScope.Capture(model, DatabaseType.SQLite, null).Resolve(live, SchemaIdentifierRules.SQLite);
        var differences = new SchemaComparer(SchemaIdentifierRules.SQLite, evidence).CompareRuntime(selected.Model, selected.Database);
        await Assert.That(differences.Select(difference => difference.Kind).ToArray()).IsEquivalentTo([
            SchemaDifferenceKind.MissingColumn, SchemaDifferenceKind.ColumnNullabilityMismatch, SchemaDifferenceKind.MissingIndex]);
        await Assert.That(differences.Select(difference => difference.Path).ToArray()).IsEquivalentTo([
            "observed.genuinely_missing", "observed.value", "observed.ix_absent"]);
        await Assert.That(evidence.Issues.Count).IsEqualTo(3);
        await Assert.That(model.TableModels.Single().Table.Columns.Count).IsEqualTo(4);
        await Assert.That(SchemaComparer.Compare(model, live, DatabaseType.SQLite).Count).IsEqualTo(6);
    }

    [Test]
    public async Task RuntimeLimitations_SnapshotScopesDeduplicatesAndPreservesUnknownEvidence()
    {
        var collector = new SchemaReadLimitations();
        var issue = new SchemaReadLimitation("selected", "value", SchemaReadAspect.ColumnDefault, SchemaReadLimitationReason.UnsupportedDefault, "Original SQL 'A' and context.");
        collector.Add(new("outside", "value", SchemaReadAspect.ColumnDefault, SchemaReadLimitationReason.UnsupportedDefault, "Outside scope."));
        collector.Add(issue);
        collector.Add(issue);
        collector.Add(new(null, null, SchemaReadAspect.Unknown, SchemaReadLimitationReason.UnsupportedDefault, "Unknown owner must remain visible."));
        collector.Add(new("selected", null, SchemaReadAspect.Unknown, SchemaReadLimitationReason.UnrepresentedIndexColumn, "Known owner, unknown object."));
        var evidence = collector.Capture(SchemaIdentifierRules.SQLite, ["SELECTED"]);
        collector.Add(new("selected", "id", SchemaReadAspect.Column, SchemaReadLimitationReason.GeneratedColumn, "Later read mutation."));
        await Assert.That(evidence.Issues.Count).IsEqualTo(3);
        await Assert.That(evidence.IsDefaultOmitted("SELECTED", "VALUE")).IsTrue();
        await Assert.That(evidence.IsColumnOmitted("selected", "value")).IsFalse();
        await Assert.That(evidence.IsIndexOmitted("selected", "id")).IsFalse();
        await Assert.That(evidence.IsColumnOmitted("selected", "id")).IsFalse();
        await Assert.That(evidence.Issues.Any(item => item.Message == issue.Diagnostic.Message)).IsTrue();
        await Assert.That(() => ((IList<DataLinqDiagnosticIssue>)evidence.Issues).Clear()).Throws<NotSupportedException>();
        await Assert.That(() => ((IList<string>)evidence.Issues[0].ContextMessages).Add("mutation")).Throws<NotSupportedException>();
    }

    [Test]
    public async Task RuntimeLimitations_RelationEvidenceUsesTypedIdentityAndCopiesColumnLists()
    {
        var model = RuntimeRelations("parent", "child", "id", "parent_id", "ix_parent", "fk_named_in_model");
        var relation = model.TableModels.Single(table => table.Table.DbName == "child").Table.ColumnIndices
            .SelectMany(index => index.RelationParts).Select(part => part.Relation).Distinct().Single();
        var source = new[] { "parent_id" };
        var target = new[] { "id" };
        var collector = new SchemaReadLimitations();
        collector.Add(new("child", "0", SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
            "Referenced object not represented.", source, "parent", target));
        source[0] = "wrong";
        target[0] = "wrong";
        await Assert.That(collector.Capture(SchemaIdentifierRules.SQLite, ["child"]).IsForeignKeyOmitted(relation)).IsTrue();
        await Assert.That(collector.Capture(SchemaIdentifierRules.SQLite, ["parent"]).IsForeignKeyOmitted(relation)).IsFalse();
        await Assert.That(collector.Capture(SchemaIdentifierRules.Server(DatabaseType.MySQL, 1), ["child"]).IsForeignKeyOmitted(relation)).IsFalse();
        var server = new SchemaReadLimitations();
        server.Add(new("child", "fk_named_in_model", SchemaReadAspect.ForeignKey, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
            "A different target is already known.", ["parent_id"], "different_parent", ["id"]));
        await Assert.That(server.Capture(SchemaIdentifierRules.Server(DatabaseType.MySQL, 1), ["child"]).IsForeignKeyOmitted(relation)).IsFalse();
    }
}
