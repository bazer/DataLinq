using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class SchemaDependencyTests
{
    [Test]
    [Arguments(1, true)]
    [Arguments(1, false)]
    [Arguments(2, true)]
    [Arguments(2, false)]
    [Arguments(3, true)]
    [Arguments(3, false)]
    public async Task SQLiteCycles_CreateUsableSchemaWithEnforcedForeignKeys(int count, bool restrict)
    {
        var metadata = CreateMetadata(count, cyclic: true);
        var factory = new SqlFromSQLiteFactory();
        var first = factory.GetCreateTables(metadata, restrict).ValueOrException().Text;
        var second = factory.GetCreateTables(metadata, restrict).ValueOrException().Text;
        // The generated header includes the current time; compare the schema itself.
        await Assert.That(first.Substring(first.IndexOf("*/", StringComparison.Ordinal) + 2))
            .IsEqualTo(second.Substring(second.IndexOf("*/", StringComparison.Ordinal) + 2));
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        connection.Open();
        Execute(first);
        for (var i = 0; i < count; i++) Execute($"INSERT INTO node{i} (id) VALUES (1)");
        for (var i = 0; i < count; i++) Execute($"UPDATE node{i} SET next_id = 1");
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check";
        using (var reader = check.ExecuteReader()) await Assert.That(reader.Read()).IsFalse();
        await Assert.That(() => Execute("UPDATE node0 SET next_id = 99")).Throws<SqliteException>();
        void Execute(string text) { using var command = connection.CreateCommand(); command.CommandText = text; command.ExecuteNonQuery(); }
    }

    [Test]
    [Arguments(2, true)]
    [Arguments(2, false)]
    [Arguments(3, true)]
    [Arguments(3, false)]
    public async Task ServerCycles_ReturnDiagnosticWithoutEmittingPartialSql(int count, bool restrict)
    {
        foreach (var provider in new[] { DatabaseType.MySQL, DatabaseType.MariaDB })
        {
            var result = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider).GetCreateTables(CreateMetadata(count, cyclic: true), restrict);
            await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
            await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.NotImplemented);
            await Assert.That(failure.Message).Contains(provider.ToString());
            await Assert.That(failure.Message).Contains("node0 -> node1");
            await Assert.That(failure.Message).Contains("cyclic foreign key constraints");
        }
    }

    [Test]
    public async Task AcyclicChain_OrdersEveryDependencyBeforeItsDependent()
    {
        var tables = CreateMetadata(128, cyclic: false).TableModels.Select(x => x.Table).ToList();
        var result = new SqlGeneration().SortTablesByForeignKeys(tables);
        await Assert.That(result.Select(x => x.DbName).ToArray())
            .IsEquivalentTo(Enumerable.Range(0, 128).Select(x => $"node{x}"));
        for (var i = 0; i < result.Count; i++)
            await Assert.That(result[i].DbName).IsEqualTo($"node{127 - i}");
    }

    [Test]
    public async Task ViewCycle_ReturnsDiagnosticOnAllSqlFactories()
    {
        var metadata = CreateViews(cyclic: true);
        var results = new[] { new SqlFromSQLiteFactory().GetCreateTables(metadata, true),
            SqlFromMetadataFactory.GetFactoryFromDatabaseType(DatabaseType.MySQL).GetCreateTables(metadata, true),
            SqlFromMetadataFactory.GetFactoryFromDatabaseType(DatabaseType.MariaDB).GetCreateTables(metadata, true) };
        foreach (var result in results)
        {
            await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
            await Assert.That(failure.Message).Contains("Cyclic view dependencies");
            await Assert.That(failure.Message).Contains("view0 -> view1 -> view0");
        }
    }

    [Test]
    public async Task AcyclicViews_CreateReferencedViewFirst()
    {
        var ddl = new SqlFromSQLiteFactory().GetCreateTables(CreateViews(cyclic: false), true).ValueOrException().Text;
        await Assert.That(ddl.IndexOf("CREATE VIEW IF NOT EXISTS \"view1\"", StringComparison.Ordinal)
            < ddl.IndexOf("CREATE VIEW IF NOT EXISTS \"view0\"", StringComparison.Ordinal)).IsTrue();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = ddl + "SELECT id FROM view0";
        await Assert.That(Convert.ToInt64(command.ExecuteScalar())).IsEqualTo(1L);
    }

    private static DatabaseDefinition CreateMetadata(int count, bool cyclic)
    {
        var tables = Enumerable.Range(0, count).Select(i =>
        {
            var properties = new System.Collections.Generic.List<MetadataValuePropertyDraft>
            {
                new("Id", new CsTypeDeclaration(typeof(int)), new MetadataColumnDraft("id")
                { PrimaryKey = true, DbTypes = Types() }) { Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("id")] }
            };
            if (cyclic || i != count - 1)
                properties.Add(new("NextId", new CsTypeDeclaration(typeof(int)), new MetadataColumnDraft("next_id")
                { ForeignKey = true, Nullable = true, DbTypes = Types() })
                { CsNullable = true, Attributes = [new ColumnAttribute("next_id"), new NullableAttribute(), new ForeignKeyAttribute($"node{(i + 1) % count}", "id", $"FK_Node{i}")] });
            return new MetadataTableModelDraft($"Nodes{i}", new MetadataModelDraft(new($"Node{i}", "SchemaDependencies", ModelCsType.Class))
            { ValueProperties = properties }, new($"node{i}"));
        }).ToArray();
        return new MetadataDefinitionFactory().Build(new MetadataDatabaseDraft("DependenciesDb", new("DependenciesDb", "SchemaDependencies", ModelCsType.Class))
        { TableModels = tables }).ValueOrException();
        static DatabaseColumnType[] Types() => [new(DatabaseType.SQLite, "integer"), new(DatabaseType.MySQL, "int"), new(DatabaseType.MariaDB, "int")];
    }

    private static DatabaseDefinition CreateViews(bool cyclic)
        => new MetadataDefinitionFactory().Build(new MetadataDatabaseDraft("ViewsDb", new("ViewsDb", "SchemaDependencies", ModelCsType.Class))
        {
            TableModels = Enumerable.Range(0, 2).Select(i => new MetadataTableModelDraft($"View{i}",
                new MetadataModelDraft(new($"View{i}", "SchemaDependencies", ModelCsType.Class))
                { ValueProperties = [new("Id", new(typeof(int)), new("id") { DbTypes = [new(DatabaseType.SQLite, "integer")] })] },
                new($"view{i}") { Type = TableType.View, Definition = i == 0 ? "SELECT id FROM view1" : cyclic ? "SELECT id FROM view0" : "SELECT 1 AS id" })).ToArray()
        }).ValueOrException();
}
