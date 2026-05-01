using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public class SchemaComparerTests
{
    [Test]
    public async Task Compare_TablePresence_ReturnsDeterministicDifferences()
    {
        var model = CreateDatabase(("account", ["id"]), ("invoice", ["id"]));
        var database = CreateDatabase(("account", ["id"]), ("audit_log", ["id"]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingTable, SchemaDifferenceKind.ExtraTable]);
        await Assert.That(differences.Select(x => x.Path).ToArray())
            .IsEquivalentTo(["invoice", "audit_log"]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.MissingTable).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Additive);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraTable).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Destructive);
    }

    [Test]
    public async Task Compare_ColumnPresence_ReturnsDeterministicDifferences()
    {
        var model = CreateDatabase(("account", ["id", "display_name"]));
        var database = CreateDatabase(("account", ["id", "legacy_name"]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingColumn, SchemaDifferenceKind.ExtraColumn]);
        await Assert.That(differences.Select(x => x.Path).ToArray())
            .IsEquivalentTo(["account.display_name", "account.legacy_name"]);
    }

    [Test]
    public async Task Compare_SQLiteIdentifiers_AreCaseInsensitiveForPresence()
    {
        var model = CreateDatabase(("Account", ["Id", "Display_Name"]));
        var database = CreateDatabase(("account", ["id", "display_name"]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Capabilities_ReflectPhaseFourSupportBoundary()
    {
        var sqlite = SchemaValidationCapabilities.For(DatabaseType.SQLite);
        var mariaDb = SchemaValidationCapabilities.For(DatabaseType.MariaDB);

        await Assert.That(sqlite.TableNameComparison).IsEqualTo(SchemaIdentifierComparison.OrdinalIgnoreCase);
        await Assert.That(sqlite.CompareIndexes).IsTrue();
        await Assert.That(sqlite.CompareChecks).IsFalse();
        await Assert.That(sqlite.CompareComments).IsFalse();

        await Assert.That(mariaDb.TableNameComparison).IsEqualTo(SchemaIdentifierComparison.RequiresProviderConfiguration);
        await Assert.That(mariaDb.ColumnNameComparison).IsEqualTo(SchemaIdentifierComparison.OrdinalIgnoreCase);
        await Assert.That(mariaDb.CompareIndexes).IsTrue();
        await Assert.That(mariaDb.CompareChecks).IsTrue();
        await Assert.That(mariaDb.CompareComments).IsTrue();
    }

    private static DatabaseDefinition CreateDatabase(params (string tableName, string[] columns)[] tables)
    {
        var database = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "DataLinq.Tests", ModelCsType.Class));
        var tableModels = tables.Select(table =>
        {
            var model = new ModelDefinition(new CsTypeDeclaration(ToCsName(table.tableName), "DataLinq.Tests", ModelCsType.Class));
            var tableDefinition = new TableDefinition(table.tableName);
            var tableModel = new TableModel(ToCsName(table.tableName), database, model, tableDefinition);
            var columns = table.columns.Select((columnName, index) =>
            {
                var property = new ValueProperty(ToCsName(columnName), new CsTypeDeclaration(typeof(int)), model, []);
                var column = new ColumnDefinition(columnName, tableDefinition);
                column.SetIndex(index);
                column.AddDbType(new DatabaseColumnType(DatabaseType.SQLite, "integer"));
                column.SetValueProperty(property);
                model.AddProperty(property);
                return column;
            });

            tableDefinition.SetColumns(columns);
            return tableModel;
        });

        database.SetTableModels(tableModels);
        return database;
    }

    private static string ToCsName(string value)
    {
        return string.Join(
            "",
            value.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)));
    }
}
