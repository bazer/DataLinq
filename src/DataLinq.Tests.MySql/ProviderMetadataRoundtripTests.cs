using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Testing;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class ProviderMetadataRoundtripTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CreateReadGenerateCreateRead_PreservesFirstSliceSupportedSubset(TestProviderDescriptor provider)
    {
        using var source = ServerSchemaDatabase.Create(
            provider,
            nameof(CreateReadGenerateCreateRead_PreservesFirstSliceSupportedSubset),
            FirstSliceSchemaStatements);

        var firstRead = source.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(CreateReadGenerateCreateRead_PreservesFirstSliceSupportedSubset)}_Generated",
            generatedSql.Text);

        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, provider.DatabaseType);
        var validationDifferences = SchemaComparer.Compare(firstRead, secondRead, provider.DatabaseType);

        await Assert.That(differences).IsEmpty();
        await Assert.That(validationDifferences).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_GeneratedModelSource_PreservesProviderMetadataShape(TestProviderDescriptor provider)
    {
        using var source = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_GeneratedModelSource_PreservesProviderMetadataShape),
            FirstSliceSchemaStatements);

        var providerMetadata = source.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var sourceMetadata = MetadataSourceRoundtrip.ParseGeneratedModelSource(providerMetadata);

        await Assert.That(MetadataEquivalenceDigest.CreateText(sourceMetadata, includeDatabaseStorageName: false))
            .IsEqualTo(MetadataEquivalenceDigest.CreateText(providerMetadata, includeDatabaseStorageName: false));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_LiveSchemaDrift_ProducesValidationDifferences(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_LiveSchemaDrift_ProducesValidationDifferences),
            """
            CREATE TABLE `account` (
                `id` INT PRIMARY KEY AUTO_INCREMENT,
                `display_name` VARCHAR(64) NOT NULL DEFAULT 'anonymous'
            );
            """);

        var model = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });

        schema.ExecuteNonQuery("ALTER TABLE `account` ADD COLUMN `nickname` VARCHAR(64) NULL;");
        schema.ExecuteNonQuery("CREATE INDEX `idx_account_nickname` ON `account` (`nickname`);");
        schema.ExecuteNonQuery(
            """
            CREATE TABLE `audit_log` (
                `id` INT PRIMARY KEY AUTO_INCREMENT
            );
            """);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = SchemaComparer.Compare(model, database, provider.DatabaseType);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([
                SchemaDifferenceKind.ExtraColumn,
                SchemaDifferenceKind.ExtraIndex,
                SchemaDifferenceKind.ExtraTable
            ]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraColumn).Path)
            .IsEqualTo("account.nickname");
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraIndex).Path)
            .IsEqualTo("account.idx_account_nickname");
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraTable).Path)
            .IsEqualTo("audit_log");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_FirstSliceSchema_CapturesIdentifiersIndexesAndDuplicateRelations(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_FirstSliceSchema_CapturesIdentifiersIndexesAndDuplicateRelations),
            FirstSliceSchemaStatements);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var account = database.TableModels.Single(x => x.Table.DbName == "account").Table;
        var invoice = database.TableModels.Single(x => x.Table.DbName == "invoice").Table;
        var accountProfile = database.TableModels.Single(x => x.Table.DbName == "account_profile").Table;

        await Assert.That(account.Columns.Any(x => x.DbName == "account id" && x.ValueProperty.PropertyName == "AccountId")).IsTrue();
        await Assert.That(account.Columns.Any(x => x.DbName == "select" && x.ValueProperty.PropertyName == "Select")).IsTrue();
        await Assert.That(account.Columns.Any(x => x.DbName == "display name" && x.ValueProperty.PropertyName == "DisplayName")).IsTrue();

        await Assert.That(accountProfile.Columns.Single(x => x.DbName == "account id").PrimaryKey).IsTrue();
        await Assert.That(accountProfile.Columns.Single(x => x.DbName == "account id").ForeignKey).IsTrue();

        await Assert.That(invoice.ColumnIndices.Any(x =>
            x.Name == "idx_invoice_created_by" &&
            x.Characteristic == IndexCharacteristic.Simple &&
            x.Columns.Select(c => c.DbName).SequenceEqual(["created by account id"]))).IsTrue();

        await Assert.That(invoice.Columns.Count(x => x.ForeignKey)).IsEqualTo(3);
        await Assert.That(invoice.Model.RelationProperties.Keys.OrderBy(x => x).ToArray())
            .IsEquivalentTo(["ApprovedByAccount", "CreatedByAccount", "ExternalAccount"]);
        await Assert.That(account.Model.RelationProperties.Keys.OrderBy(x => x).ToArray())
            .IsEquivalentTo(["AccountProfile", "InvoiceApprovedBy", "InvoiceCreatedBy", "InvoiceExternalAccount"]);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_CompositeUniqueIndex_GeneratesClassLevelIndexWithDatabaseColumnNames(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_CompositeUniqueIndex_GeneratesClassLevelIndexWithDatabaseColumnNames),
            """
            CREATE TABLE `account` (
                `id` INT PRIMARY KEY AUTO_INCREMENT,
                `RakenskapsarFK` INT NOT NULL,
                `Kontonummer` INT NOT NULL,
                UNIQUE INDEX `IX_Account_RakenskapsarFK_Kontonummer` (`RakenskapsarFK`, `Kontonummer`)
            );
            """);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "Account.cs");

        await Assert.That(generatedFile.contents).Contains("[Index(\"IX_Account_RakenskapsarFK_Kontonummer\", IndexCharacteristic.Unique, IndexType.BTREE, \"RakenskapsarFK\", \"Kontonummer\")]");
        await Assert.That(generatedFile.contents).DoesNotContain("[Index(\"IX_Account_RakenskapsarFK_Kontonummer\", IndexCharacteristic.Unique, IndexType.BTREE, \"RakenskapsarFK\", \"Kontonummer\")]\n    [Column(\"RakenskapsarFK\")]");
        await Assert.That(generatedFile.contents).DoesNotContain("[Index(\"IX_Account_RakenskapsarFK_Kontonummer\", IndexCharacteristic.Unique, IndexType.BTREE, \"RakenskapsarFK\", \"Kontonummer\")]\n    [Column(\"Kontonummer\")]");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_TableAndColumnComments_GeneratesCommentAttributes(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_TableAndColumnComments_GeneratesCommentAttributes),
            """
            CREATE TABLE `commented_account` (
                `id` INT PRIMARY KEY AUTO_INCREMENT COMMENT 'Identifier column',
                `display_name` VARCHAR(64) NOT NULL COMMENT 'Column "display" owner''s label'
            ) COMMENT='Table "account" owner''s comment';
            """);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var account = database.TableModels.Single(x => x.Table.DbName == "commented_account");
        var displayName = account.Table.Columns.Single(x => x.DbName == "display_name");
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "CommentedAccount.cs");
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(database, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(ParseDatabase_TableAndColumnComments_GeneratesCommentAttributes)}_Generated",
            generatedSql.Text);
        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(database, secondRead, provider.DatabaseType);

        await Assert.That(account.Model.Attributes.OfType<CommentAttribute>().Single().Text).IsEqualTo("Table \"account\" owner's comment");
        await Assert.That(displayName.ValueProperty.Attributes.OfType<CommentAttribute>().Single().Text).IsEqualTo("Column \"display\" owner's label");
        await Assert.That(generatedFile.contents).Contains("[Comment(\"Table \\\"account\\\" owner's comment\")]");
        await Assert.That(generatedFile.contents).Contains("[Comment(\"Column \\\"display\\\" owner's label\")]");
        await Assert.That(generatedSql.Text).Contains("COMMENT='Table \"account\" owner''s comment'");
        await Assert.That(generatedSql.Text).Contains("COMMENT 'Column \"display\" owner''s label'");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_CheckConstraints_RoundTripAsRawProviderAttributes(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_CheckConstraints_RoundTripAsRawProviderAttributes),
            """
            CREATE TABLE `checked_account` (
                `id` INT PRIMARY KEY,
                `amount` INT NOT NULL,
                `status` VARCHAR(16) NOT NULL,
                CONSTRAINT `CK_checked_account_amount` CHECK (`amount` >= 0),
                CONSTRAINT `CK_checked_account_status` CHECK (`status` IN ('active', 'hold'))
            );
            """);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var account = database.TableModels.Single(x => x.Table.DbName == "checked_account");
        var amountCheck = account.Model.Attributes.OfType<CheckAttribute>().Single(x => x.Name == "CK_checked_account_amount");
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "CheckedAccount.cs");
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(database, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(ParseDatabase_CheckConstraints_RoundTripAsRawProviderAttributes)}_Generated",
            generatedSql.Text);
        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(database, secondRead, provider.DatabaseType);

        await Assert.That(amountCheck.DatabaseType).IsEqualTo(provider.DatabaseType);
        await Assert.That(amountCheck.Expression).Contains("amount");
        await Assert.That(generatedFile.contents).Contains($"[Check(DatabaseType.{provider.DatabaseType}, \"CK_checked_account_amount\",");
        await Assert.That(generatedSql.Text).Contains("CONSTRAINT `CK_checked_account_amount` CHECK");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_QuotedIdentifiers_RoundTripWithStableCSharpNames(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_QuotedIdentifiers_RoundTripWithStableCSharpNames),
            """
            CREATE TABLE `order-items` (
                `order-id` INT PRIMARY KEY,
                `class` VARCHAR(16) NOT NULL,
                `ship.to` VARCHAR(64) NOT NULL,
                `2fa code` VARCHAR(6) NOT NULL,
                `total$amount` INT NOT NULL,
                UNIQUE INDEX `UX_order-items_ship.to` (`ship.to`)
            );
            """);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var orderItems = database.TableModels.Single(x => x.Table.DbName == "order-items");
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "OrderItems.cs");
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(database, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(ParseDatabase_QuotedIdentifiers_RoundTripWithStableCSharpNames)}_Generated",
            generatedSql.Text);
        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(database, secondRead, provider.DatabaseType);

        await Assert.That(orderItems.Model.CsType.Name).IsEqualTo("OrderItems");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "order-id").ValueProperty.PropertyName).IsEqualTo("OrderId");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "class").ValueProperty.PropertyName).IsEqualTo("Class");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "ship.to").ValueProperty.PropertyName).IsEqualTo("ShipTo");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "2fa code").ValueProperty.PropertyName).IsEqualTo("_2faCode");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "total$amount").ValueProperty.PropertyName).IsEqualTo("TotalAmount");
        await Assert.That(generatedFile.contents).Contains("[Table(\"order-items\")]");
        await Assert.That(generatedFile.contents).Contains("[Column(\"ship.to\")]");
        await Assert.That(generatedFile.contents).Contains("[Column(\"2fa code\")]");
        await Assert.That(generatedSql.Text).Contains("CREATE TABLE IF NOT EXISTS `order-items`");
        await Assert.That(generatedSql.Text).Contains("`ship.to`");
        await Assert.That(generatedSql.Text).Contains("`2fa code`");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_CompositeForeignKey_RoundTripAsSingleRelation(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_CompositeForeignKey_RoundTripAsSingleRelation),
            """
            CREATE TABLE `order_header` (
                `tenant_id` INT NOT NULL,
                `order_no` INT NOT NULL,
                `summary` VARCHAR(64) NOT NULL,
                PRIMARY KEY (`tenant_id`, `order_no`)
            );
            """,
            """
            CREATE TABLE `order_line` (
                `line_id` INT PRIMARY KEY,
                `tenant_id` INT NOT NULL,
                `order_no` INT NOT NULL,
                `sku` VARCHAR(64) NOT NULL,
                CONSTRAINT `FK_order_line_header` FOREIGN KEY (`tenant_id`, `order_no`) REFERENCES `order_header`(`tenant_id`, `order_no`)
            );
            """);

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var orderLine = database.TableModels.Single(x => x.Table.DbName == "order_line");
        var foreignKeyIndex = orderLine.Table.ColumnIndices.Single(x => x.Characteristic == IndexCharacteristic.ForeignKey);
        var foreignKeyPart = foreignKeyIndex.RelationParts.Single(x => x.Type == RelationPartType.ForeignKey);
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "OrderLine.cs");
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(database, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(ParseDatabase_CompositeForeignKey_RoundTripAsSingleRelation)}_Generated",
            generatedSql.Text);
        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(database, secondRead, provider.DatabaseType);

        await Assert.That(foreignKeyIndex.Columns.Select(x => x.DbName).SequenceEqual(["tenant_id", "order_no"])).IsTrue();
        await Assert.That(foreignKeyPart.Relation.CandidateKey.ColumnIndex.Columns.Select(x => x.DbName).SequenceEqual(["tenant_id", "order_no"])).IsTrue();
        await Assert.That(orderLine.Model.RelationProperties.Keys).Contains("OrderHeader");
        await Assert.That(generatedFile.contents).Contains("[ForeignKey(\"order_header\", \"tenant_id\", \"FK_order_line_header\", 1");
        await Assert.That(generatedFile.contents).Contains("[ForeignKey(\"order_header\", \"order_no\", \"FK_order_line_header\", 2");
        await Assert.That(generatedFile.contents).Contains("[Relation(\"order_header\", new string[] { \"tenant_id\", \"order_no\" }, \"FK_order_line_header\")]");
        await Assert.That(generatedSql.Text).Contains("FOREIGN KEY (`tenant_id`, `order_no`) REFERENCES `order_header` (`tenant_id`, `order_no`)");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_ForeignKeyReferentialActions_RoundTrip(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_ForeignKeyReferentialActions_RoundTrip),
            """
            CREATE TABLE `account` (
                `id` INT PRIMARY KEY
            );
            """,
            """
            CREATE TABLE `invoice` (
                `id` INT PRIMARY KEY,
                `account_id` INT NULL,
                CONSTRAINT `FK_invoice_account` FOREIGN KEY (`account_id`) REFERENCES `account`(`id`) ON UPDATE CASCADE ON DELETE SET NULL
            );
            """);

        var firstRead = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var invoice = firstRead.TableModels.Single(x => x.Table.DbName == "invoice").Table;
        var relation = invoice.ColumnIndices
            .SelectMany(x => x.RelationParts)
            .Single(x => x.Type == RelationPartType.ForeignKey)
            .Relation;
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(firstRead)
            .Single(file => file.path == "Invoice.cs");
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(ParseDatabase_ForeignKeyReferentialActions_RoundTrip)}_Generated",
            generatedSql.Text);
        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, provider.DatabaseType);

        await Assert.That(relation.OnUpdate).IsEqualTo(ReferentialAction.Cascade);
        await Assert.That(relation.OnDelete).IsEqualTo(ReferentialAction.SetNull);
        await Assert.That(generatedFile.contents).Contains("[ForeignKey(\"account\", \"id\", \"FK_invoice_account\", ReferentialAction.Cascade, ReferentialAction.SetNull)]");
        await Assert.That(generatedSql.Text).Contains("ON UPDATE CASCADE ON DELETE SET NULL");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_UnsupportedPrefixIndexesWarnAndSkip(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_UnsupportedPrefixIndexesWarnAndSkip),
            """
            CREATE TABLE `prefix_indexed_account` (
                `id` INT PRIMARY KEY,
                `display_name` VARCHAR(64) NOT NULL,
                INDEX `idx_prefix_display_name` (`display_name`(8))
            );
            """);
        var warnings = new List<string>();

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true, Log = warnings.Add });
        var table = database.TableModels.Single(x => x.Table.DbName == "prefix_indexed_account").Table;

        await Assert.That(table.ColumnIndices.Any(x => x.Name == "idx_prefix_display_name")).IsFalse();
        await Assert.That(warnings.Any(x =>
            x.Contains($"Skipping unsupported {provider.DatabaseType} prefix-length index 'idx_prefix_display_name'", System.StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_GeneratedColumnsWarnAndSkip(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_GeneratedColumnsWarnAndSkip),
            """
            CREATE TABLE `generated_account` (
                `id` INT PRIMARY KEY,
                `first_name` VARCHAR(32) NOT NULL,
                `last_name` VARCHAR(32) NOT NULL,
                `full_name` VARCHAR(65) GENERATED ALWAYS AS (CONCAT(`first_name`, ' ', `last_name`)) STORED
            );
            """);
        var warnings = new List<string>();

        var database = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true, Log = warnings.Add });
        var table = database.TableModels.Single(x => x.Table.DbName == "generated_account").Table;

        await Assert.That(table.Columns.Any(x => x.DbName == "full_name")).IsFalse();
        await Assert.That(warnings.Any(x =>
            x.Contains($"Skipping unsupported {provider.DatabaseType} generated column 'generated_account.full_name'", System.StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_RawExpressionDefaults_RoundTripAsProviderSql(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_RawExpressionDefaults_RoundTripAsProviderSql),
            """
            CREATE TABLE `raw_default_account` (
                `id` INT PRIMARY KEY,
                `counter` INT NOT NULL DEFAULT (0 + 1)
            );
            """);

        var firstRead = schema.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var counter = firstRead.TableModels
            .Single(x => x.Table.DbName == "raw_default_account")
            .Table.Columns.Single(x => x.DbName == "counter");
        var defaultSql = (DefaultSqlAttribute)counter.ValueProperty.GetDefaultAttribute()!;
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(firstRead)
            .Single(file => file.path == "RawDefaultAccount.cs");
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = ServerSchemaDatabase.Create(
            provider,
            $"{nameof(ParseDatabase_RawExpressionDefaults_RoundTripAsProviderSql)}_Generated",
            generatedSql.Text);
        var secondRead = generated.ParseDatabase(
            "RoundtripDb",
            "RoundtripDb",
            "DataLinq.Tests.Roundtrip",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, provider.DatabaseType);

        await Assert.That(defaultSql.DatabaseType).IsEqualTo(provider.DatabaseType);
        await Assert.That(defaultSql.Expression).Contains("0 + 1");
        await Assert.That(generatedFile.contents).Contains($"[DefaultSql(DatabaseType.{provider.DatabaseType},");
        await Assert.That(generatedSql.Text).Contains("DEFAULT");
        await Assert.That(generatedSql.Text).Contains("0 + 1");
        await Assert.That(differences).IsEmpty();
    }

    private static readonly string[] FirstSliceSchemaStatements =
    [
        """
        CREATE TABLE `account` (
            `account id` INT PRIMARY KEY AUTO_INCREMENT,
            `select` VARCHAR(64) NOT NULL,
            `display name` VARCHAR(255) NOT NULL DEFAULT 'anonymous'
        );
        """,
        """
        CREATE TABLE `account_profile` (
            `account id` INT PRIMARY KEY,
            `bio` TEXT,
            CONSTRAINT `FK_profile_account` FOREIGN KEY (`account id`) REFERENCES `account`(`account id`)
        );
        """,
        """
        CREATE TABLE `invoice` (
            `invoice id` INT PRIMARY KEY,
            `created by account id` INT NOT NULL,
            `approved by account id` INT NULL,
            `external account id` INT NOT NULL,
            `number` VARCHAR(32) NOT NULL,
            INDEX `idx_invoice_created_by` (`created by account id`),
            INDEX `idx_invoice_external_account` (`external account id`),
            CONSTRAINT `FK_invoice_created_by` FOREIGN KEY (`created by account id`) REFERENCES `account`(`account id`),
            CONSTRAINT `FK_invoice_approved_by` FOREIGN KEY (`approved by account id`) REFERENCES `account`(`account id`),
            CONSTRAINT `FK_invoice_external_account` FOREIGN KEY (`external account id`) REFERENCES `account`(`account id`)
        );
        """
    ];
}
