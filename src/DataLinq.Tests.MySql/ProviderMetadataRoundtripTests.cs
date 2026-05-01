using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.MySql;
using DataLinq.Testing;
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

        await Assert.That(differences).IsEmpty();
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
                `display_name` VARCHAR(64) NOT NULL COMMENT 'Column "display" label'
            ) COMMENT='Table "account" comment';
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

        await Assert.That(account.Model.Attributes.OfType<CommentAttribute>().Single().Text).IsEqualTo("Table \"account\" comment");
        await Assert.That(displayName.ValueProperty.Attributes.OfType<CommentAttribute>().Single().Text).IsEqualTo("Column \"display\" label");
        await Assert.That(generatedFile.contents).Contains("[Comment(\"Table \\\"account\\\" comment\")]");
        await Assert.That(generatedFile.contents).Contains("[Comment(\"Column \\\"display\\\" label\")]");
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
