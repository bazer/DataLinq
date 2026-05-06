using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Validation;
using ThrowAway.Extensions;

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
        await Assert.That(sqlite.CompareColumnTypes).IsTrue();
        await Assert.That(sqlite.CompareNullability).IsTrue();
        await Assert.That(sqlite.ComparePrimaryKeys).IsTrue();
        await Assert.That(sqlite.CompareAutoIncrement).IsTrue();
        await Assert.That(sqlite.CompareDefaults).IsTrue();
        await Assert.That(sqlite.CompareIndexes).IsTrue();
        await Assert.That(sqlite.CompareChecks).IsFalse();
        await Assert.That(sqlite.CompareComments).IsFalse();

        await Assert.That(mariaDb.TableNameComparison).IsEqualTo(SchemaIdentifierComparison.RequiresProviderConfiguration);
        await Assert.That(mariaDb.ColumnNameComparison).IsEqualTo(SchemaIdentifierComparison.OrdinalIgnoreCase);
        await Assert.That(mariaDb.CompareColumnTypes).IsTrue();
        await Assert.That(mariaDb.CompareNullability).IsTrue();
        await Assert.That(mariaDb.ComparePrimaryKeys).IsTrue();
        await Assert.That(mariaDb.CompareAutoIncrement).IsTrue();
        await Assert.That(mariaDb.CompareDefaults).IsTrue();
        await Assert.That(mariaDb.CompareIndexes).IsTrue();
        await Assert.That(mariaDb.CompareChecks).IsTrue();
        await Assert.That(mariaDb.CompareComments).IsTrue();
    }

    [Test]
    public async Task Compare_ColumnShape_ReturnsAmbiguousMismatches()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("display_name", typeof(int), nullable: true, defaultValue: 1)
                ]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("display_name", typeof(int), nullable: false, autoIncrement: true, defaultValue: 2, sqliteTypeName: "text")
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([
                SchemaDifferenceKind.ColumnTypeMismatch,
                SchemaDifferenceKind.ColumnNullabilityMismatch,
                SchemaDifferenceKind.ColumnAutoIncrementMismatch,
                SchemaDifferenceKind.ColumnDefaultMismatch
            ]);
        await Assert.That(differences.All(x => x.Safety == SchemaDifferenceSafety.Ambiguous)).IsTrue();
    }

    [Test]
    public async Task Compare_DefaultCodeExpressionDifference_DoesNotCreateSchemaDrift()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn(
                        "display_name",
                        typeof(string),
                        nullable: false,
                        attributes: [new DefaultAttribute("anonymous").SetCodeExpression("\"anonymous\"")])
                ]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn(
                        "display_name",
                        typeof(string),
                        nullable: false,
                        attributes: [new DefaultAttribute("anonymous")])
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Compare_RawProviderDefaults_CompareProviderExpression()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn(
                        "payload",
                        typeof(string),
                        nullable: false,
                        attributes: [new DefaultSqlAttribute(DatabaseType.MySQL, "(json_object())")])
                ]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn(
                        "payload",
                        typeof(string),
                        nullable: false,
                        attributes: [new DefaultSqlAttribute(DatabaseType.MySQL, "(json_array())")])
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnDefaultMismatch]);
    }

    [Test]
    public async Task Compare_Indexes_ReturnsAdditiveAndDestructiveDifferences()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("accounting_year", typeof(int), nullable: false),
                    CreateColumn("account_number", typeof(int), nullable: false)
                ],
                [new IndexAttribute("ux_account_year_number", IndexCharacteristic.Unique, IndexType.BTREE, "accounting_year", "account_number")]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("accounting_year", typeof(int), nullable: false),
                    CreateColumn("account_number", typeof(int), nullable: false)
                ],
                [new IndexAttribute("idx_account_year", IndexCharacteristic.Simple, IndexType.BTREE, "accounting_year")]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingIndex, SchemaDifferenceKind.ExtraIndex]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.MissingIndex).Severity)
            .IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraIndex).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Destructive);
    }

    [Test]
    public async Task Compare_ForeignKeys_ReturnsOrderedRelationDifferences()
    {
        var model = CreateDatabase(
            CreateTable(
                "order_header",
                [
                    CreateColumn("tenant_id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("order_no", typeof(int), nullable: false, primaryKey: true)
                ]),
            CreateTable(
                "order_line",
                [
                    CreateColumn(
                        "tenant_id",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new ForeignKeyAttribute("order_header", "tenant_id", "FK_order_line_header", 0)]),
                    CreateColumn(
                        "order_no",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new ForeignKeyAttribute("order_header", "order_no", "FK_order_line_header", 1)])
                ]));
        var database = CreateDatabase(
            CreateTable(
                "order_header",
                [
                    CreateColumn("tenant_id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("order_no", typeof(int), nullable: false, primaryKey: true)
                ]),
            CreateTable(
                "order_line",
                [
                    CreateColumn("tenant_id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("order_no", typeof(int), nullable: false, primaryKey: true)
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingForeignKey]);
        await Assert.That(differences.Single().Path).IsEqualTo("order_line.FK_order_line_header");
        await Assert.That(differences.Single().Safety).IsEqualTo(SchemaDifferenceSafety.Additive);
    }

    [Test]
    public async Task Compare_ForeignKeyActions_ArePartOfRelationSignature()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable(
                "invoice",
                [
                    CreateColumn(
                        "account_id",
                        typeof(int),
                        nullable: true,
                        primaryKey: true,
                        attributes: [new ForeignKeyAttribute("account", "id", "FK_invoice_account", ReferentialAction.Cascade, ReferentialAction.SetNull)])
                ]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable(
                "invoice",
                [
                    CreateColumn(
                        "account_id",
                        typeof(int),
                        nullable: true,
                        primaryKey: true,
                        attributes: [new ForeignKeyAttribute("account", "id", "FK_invoice_account", ReferentialAction.NoAction, ReferentialAction.NoAction)])
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingForeignKey, SchemaDifferenceKind.ExtraForeignKey]);
    }

    [Test]
    public async Task Compare_Views_ComparePresenceTypeAndColumns()
    {
        var model = CreateDatabase(
            CreateTable("account", [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable(
                "current_account",
                TableType.View,
                [
                    CreateColumn("id", typeof(int), nullable: false),
                    CreateColumn("display_name", typeof(string), nullable: false)
                ]),
            CreateTable("account_lookup", TableType.View, [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]));
        var database = CreateDatabase(
            CreateTable("account", [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable("current_account", TableType.View, [CreateColumn("id", typeof(int), nullable: false)]),
            CreateTable("account_lookup", [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable("legacy_account", TableType.View, [CreateColumn("id", typeof(int), nullable: false)]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([
                SchemaDifferenceKind.TableTypeMismatch,
                SchemaDifferenceKind.MissingColumn,
                SchemaDifferenceKind.ExtraTable
            ]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.MissingColumn).Path)
            .IsEqualTo("current_account.display_name");
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraTable).Message)
            .Contains("extra view 'legacy_account'");
    }

    [Test]
    public async Task Compare_MariaDbChecksAndComments_UsesProviderSpecificMetadata()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn(
                        "id",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new CommentAttribute(DatabaseType.MariaDB, "model column comment")])
                ],
                [
                    new CheckAttribute(DatabaseType.MariaDB, "CK_account_id", "`id` > 0"),
                    new CommentAttribute(DatabaseType.MariaDB, "model table comment")
                ]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn(
                        "id",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new CommentAttribute(DatabaseType.MariaDB, "database column comment")])
                ],
                [
                    new CheckAttribute(DatabaseType.MariaDB, "CK_account_legacy", "`id` >= 0"),
                    new CommentAttribute(DatabaseType.MariaDB, "database table comment")
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MariaDB);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([
                SchemaDifferenceKind.MissingCheck,
                SchemaDifferenceKind.ExtraCheck,
                SchemaDifferenceKind.TableCommentMismatch,
                SchemaDifferenceKind.ColumnCommentMismatch
            ]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.TableCommentMismatch).Severity)
            .IsEqualTo(SchemaDifferenceSeverity.Info);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ColumnCommentMismatch).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Informational);
    }

    private static DatabaseDefinition CreateDatabase(params (string tableName, string[] columns)[] tables)
    {
        return CreateDatabase(tables
            .Select(table => CreateTable(
                table.tableName,
                table.columns
                    .Select((column, index) => CreateColumn(column, typeof(int), nullable: false, primaryKey: index == 0))
                    .ToArray()))
            .ToArray());
    }

    private static DatabaseDefinition CreateDatabase(params MetadataTableModelDraft[] tableModels)
    {
        var draft = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "DataLinq.Tests", ModelCsType.Class))
        {
            TableModels = tableModels
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static MetadataTableModelDraft CreateTable(
        string tableName,
        MetadataValuePropertyDraft[] columns,
        Attribute[]? attributes = null)
    {
        return CreateTable(tableName, TableType.Table, columns, attributes);
    }

    private static MetadataTableModelDraft CreateTable(
        string tableName,
        TableType type,
        MetadataValuePropertyDraft[] columns,
        Attribute[]? attributes = null)
    {
        return new MetadataTableModelDraft(
            ToCsName(tableName),
            new MetadataModelDraft(new CsTypeDeclaration(ToCsName(tableName), "DataLinq.Tests", ModelCsType.Class))
            {
                Attributes = attributes ?? [],
                ValueProperties = columns
            },
            new MetadataTableDraft(tableName)
            {
                Type = type,
                Definition = type == TableType.View ? $"select * from {tableName}" : null
            });
    }

    private static MetadataValuePropertyDraft CreateColumn(
        string columnName,
        Type csType,
        bool nullable,
        bool primaryKey = false,
        bool autoIncrement = false,
        object? defaultValue = null,
        Attribute[]? attributes = null,
        string? sqliteTypeName = null)
    {
        var propertyAttributes = defaultValue == null
            ? attributes ?? []
            : [new DefaultAttribute(defaultValue), .. (attributes ?? [])];

        return new MetadataValuePropertyDraft(
            ToCsName(columnName),
            new CsTypeDeclaration(csType),
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                AutoIncrement = autoIncrement,
                Nullable = nullable,
                ForeignKey = propertyAttributes.Any(static x => x is ForeignKeyAttribute),
                DbTypes =
                [
                    GetColumnType(DatabaseType.SQLite, csType, sqliteTypeName),
                    GetColumnType(DatabaseType.MySQL, csType),
                    GetColumnType(DatabaseType.MariaDB, csType)
                ]
            })
        {
            Attributes = propertyAttributes,
            CsNullable = nullable || autoIncrement
        };
    }

    private static DatabaseColumnType GetColumnType(DatabaseType databaseType, Type csType, string? sqliteTypeName = null)
    {
        if (databaseType == DatabaseType.SQLite)
        {
            return csType == typeof(string)
                ? new DatabaseColumnType(databaseType, sqliteTypeName ?? "text")
                : new DatabaseColumnType(databaseType, sqliteTypeName ?? "integer");
        }

        return csType == typeof(string)
            ? new DatabaseColumnType(databaseType, "varchar", 40)
            : new DatabaseColumnType(databaseType, "int", signed: true);
    }

    private static string ToCsName(string value)
    {
        return string.Join(
            "",
            value.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }
}
