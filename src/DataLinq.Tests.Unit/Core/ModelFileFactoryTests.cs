using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class ModelFileFactoryTests
{
    [Test]
    public async Task CreateModelFiles_DefaultStringContainingDoubleQuotes_EscapesValue()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "'\"\"'");

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        await Assert.That(generatedFile.contents).Contains("[Default(\"'\\\"\\\"'\")]");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_GuidDefault_EmitsCanonicalDefaultGuidAndReparsesToBaseMetadata()
    {
        const string uppercaseText = "00112233-4455-6677-8899-AABBCCDDEEFF";
        const string canonicalText = "00112233-4455-6677-8899-aabbccddeeff";
        var expected = Guid.ParseExact(uppercaseText, "D");
        var database = CreateDatabaseWithGuidDefault(new DefaultAttribute(expected));

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        await Assert.That(generatedFile.contents)
            .Contains($"[DefaultGuid(\"{canonicalText}\")]");
        await Assert.That(generatedFile.contents).DoesNotContain("Guid.Parse");
        await Assert.That(generatedFile.contents).DoesNotContain("new Guid");

        var root = CSharpSyntaxTree.ParseText(generatedFile.contents)
            .GetCompilationUnitRoot();
        var attributeSyntax = root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .Single(attribute => attribute.Name.ToString().Contains("DefaultGuid", StringComparison.Ordinal));
        var parser = new SyntaxParser(
            root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray());
        var parsedAttribute = parser.ParseAttribute(attributeSyntax).ValueOrException();

        await Assert.That(parsedAttribute).IsTypeOf<DefaultAttribute>();
        var defaultAttribute = (DefaultAttribute)parsedAttribute;
        await Assert.That(defaultAttribute.Value).IsTypeOf<Guid>();
        await Assert.That((Guid)defaultAttribute.Value).IsEqualTo(expected);
        await Assert.That(defaultAttribute.CodeExpression).IsNull();

        var customDatabase = CreateDatabaseWithGuidDefault(
            new CustomGuidDefaultAttribute(uppercaseText));
        NotSupportedException? customException = null;
        try
        {
            _ = new ModelFileFactory(new ModelFileFactoryOptions())
                .CreateModelFiles(customDatabase)
                .ToList();
        }
        catch (NotSupportedException exception)
        {
            customException = exception;
        }

        await Assert.That(customException).IsNotNull();
        await Assert.That(customException!.Message).Contains(nameof(CustomGuidDefaultAttribute));
        await Assert.That(customException.Message).Contains("QuoteModel.QuoteText");

        var expressionDatabase = CreateDatabaseWithGuidDefault(
            new DefaultAttribute(expected, "CustomGuidFactory.Create()"));
        NotSupportedException? expressionException = null;
        try
        {
            _ = new ModelFileFactory(new ModelFileFactoryOptions())
                .CreateModelFiles(expressionDatabase)
                .ToList();
        }
        catch (NotSupportedException exception)
        {
            expressionException = exception;
        }

        await Assert.That(expressionException).IsNotNull();
        await Assert.That(expressionException!.Message).Contains("CodeExpression");
        await Assert.That(expressionException.Message).Contains("QuoteModel.QuoteText");
    }

    [Test]
    public async Task CreateModelFiles_EmitsGeneratedPreambleAndOptionalCliStamp()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "generated");
        var stamp = new GeneratedFileStamp(
            "1.2.3-test",
            new DateTimeOffset(2026, 5, 14, 12, 30, 45, TimeSpan.Zero));

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions
        {
            UseNullableReferenceTypes = true,
            GeneratedFileStamp = stamp
        })
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        await Assert.That(generatedFile.contents).StartsWith(
            """
            // <auto-generated />
            // Generated by DataLinq. Supported model class names, property names, relation names, and C# property types may be edited.
            // See https://datalinq.org/docs/model-generation.html before changing mapping attributes or using --fresh.
            // DataLinq CLI version: 1.2.3-test
            // Generated at (UTC): 2026-05-14T12:30:45.0000000+00:00
            #nullable enable

            using System;
            """);
    }

    [Test]
    public async Task CreateModelFiles_DefaultOptions_EnableNullableReferenceTypes()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "generated");

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        await Assert.That(generatedFile.contents).StartsWith(
            """
            // <auto-generated />
            // Generated by DataLinq. Supported model class names, property names, relation names, and C# property types may be edited.
            // See https://datalinq.org/docs/model-generation.html before changing mapping attributes or using --fresh.
            #nullable enable

            using System;
            """);
    }

    [Test]
    public async Task CreateModelFiles_ExplicitNullableOptOut_EmitsNullableDisable()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "generated");

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions
        {
            UseNullableReferenceTypes = false
        })
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        await Assert.That(generatedFile.contents).StartsWith(
            """
            // <auto-generated />
            // Generated by DataLinq. Supported model class names, property names, relation names, and C# property types may be edited.
            // See https://datalinq.org/docs/model-generation.html before changing mapping attributes or using --fresh.
            #nullable disable

            using System;
            """);
    }

    [Test]
    public async Task CreateModelFiles_DatabaseModel_UsesTargetTypedDbReadInitialization()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "generated");

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteDb.cs");

        await Assert.That(generatedFile.contents).Contains("public DbRead<QuoteModel> QuoteModels { get; } = new(readSource);");
        await Assert.That(generatedFile.contents).DoesNotContain("new DbRead<QuoteModel>(dataSource)");
    }

    [Test]
    public async Task CreateModelFiles_ModelsAndDatabaseRootUseNeutralReadSource()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "generated");

        var generatedFiles = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .ToList();

        var modelFile = generatedFiles.Single(file => file.path == "QuoteModel.cs");
        await Assert.That(modelFile.contents).Contains(
            "public abstract partial class QuoteModel(IRowData rowData, IDataLinqReadSource readSource) : Immutable<QuoteModel, QuoteDb>(rowData, readSource), ITableModel<QuoteDb>");
        await Assert.That(modelFile.contents).DoesNotContain("IDataSourceAccess dataSource");

        var databaseFile = generatedFiles.Single(file => file.path == "QuoteDb.cs");
        await Assert.That(databaseFile.contents).Contains(
            "public partial class QuoteDb(IDataLinqReadSource readSource) : IDatabaseModel<QuoteDb>");
        await Assert.That(databaseFile.contents).Contains(
            "public DbRead<QuoteModel> QuoteModels { get; } = new(readSource);");
        await Assert.That(databaseFile.contents).DoesNotContain("DataSourceAccess dataSource");
    }

    [Test]
    public async Task CreateModelFiles_DefaultCharDoubleQuote_EscapesValue()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(char)), '"');

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        await Assert.That(generatedFile.contents).Contains("[Default('");
        await Assert.That(generatedFile.contents).DoesNotContain("[Default(\"");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_CompositeIndex_EmitsClassLevelIndex()
    {
        var database = CreateDatabaseWithCompositeIndex();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "Account.cs");

        await Assert.That(generatedFile.contents).Contains("[Index(\"idx_account_year_number\", IndexCharacteristic.Unique, IndexType.BTREE, \"accounting_year\", \"account_number\")]");
        await Assert.That(generatedFile.contents).DoesNotContain("[Index(\"idx_account_year_number\", IndexCharacteristic.Unique, IndexType.BTREE, \"accounting_year\", \"account_number\")]\n    [Column(\"accounting_year\")]");
        await Assert.That(generatedFile.contents).DoesNotContain("[Index(\"idx_account_year_number\", IndexCharacteristic.Unique, IndexType.BTREE, \"accounting_year\", \"account_number\")]\n    [Column(\"account_number\")]");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_Comments_EmitsEscapedCommentAttributes()
    {
        var database = CreateDatabaseWithComments();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "CommentModel.cs");

        await Assert.That(generatedFile.contents).Contains("[Comment(\"Table comment with \\\"quotes\\\"\")]");
        await Assert.That(generatedFile.contents).Contains("[Comment(\"Column comment with \\\"quotes\\\"\")]");
        await Assert.That(generatedFile.contents).Contains("/// Table comment with \"quotes\"");
        await Assert.That(generatedFile.contents).Contains("/// Column comment with \"quotes\"");
        await Assert.That(generatedFile.contents).Contains("[Check(DatabaseType.MySQL, \"CK_comment_model_name\", \"`name` <> ''\")]");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_GuidStorageAttributes_EmitStableDeclarations()
    {
        var database = CreateDatabaseWithGuidStorage();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "GuidStorageModel.cs");

        await Assert.That(generatedFile.contents).Contains(
            "[GuidStorage(GuidStorageFormat.Text36)]");
        await Assert.That(generatedFile.contents).Contains(
            "[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_DefaultNewUuid_PreservesExplicitVersions()
    {
        var database = CreateDatabaseWithUuidGenerationDefaults();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "UuidDefaultModel.cs");

        await Assert.That(generatedFile.contents).Contains(
            "[DefaultNewUUID(UUIDVersion.Version4)]");
        await Assert.That(generatedFile.contents).Contains(
            "[DefaultNewUUID(UUIDVersion.Version7)]");
        await Assert.That(generatedFile.contents).DoesNotContain("[DefaultNewUUID]");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_MetadataStringArguments_EscapeLiterals()
    {
        var database = CreateDatabaseWithEscapedMetadataStrings();
        var generatedFiles = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .ToList();

        var databaseFile = generatedFiles.Single(file => file.path == "EscapedDb.cs");
        await Assert.That(databaseFile.contents).Contains("[Database(\"Escaped\\\"Db\")]");

        var modelFile = generatedFiles.Single(file => file.path == "EscapedModel.cs");

        await Assert.That(modelFile.contents).Contains("[Table(\"order\\\"items\")]");
        await Assert.That(modelFile.contents).Contains("[Index(\"idx\\\"ship\", IndexCharacteristic.Unique, IndexType.BTREE, \"ship\\\"to\", \"2fa\\\\code\")]");
        await Assert.That(modelFile.contents).Contains("[Type(DatabaseType.MySQL, \"var\\\"char\", 255)]");
        await Assert.That(modelFile.contents).Contains("[Column(\"ship\\\"to\")]");
        await Assert.That(modelFile.contents).Contains("[Column(\"2fa\\\\code\")]");

        var databaseSyntaxTree = CSharpSyntaxTree.ParseText(databaseFile.contents);
        var databaseSyntaxErrors = databaseSyntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(databaseSyntaxErrors).IsEmpty();

        var modelSyntaxTree = CSharpSyntaxTree.ParseText(modelFile.contents);
        var modelSyntaxErrors = modelSyntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(modelSyntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_ViewStringArguments_EscapeLiterals()
    {
        var database = CreateDatabaseWithEscapedViewStrings();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "EscapedViewModel.cs");

        await Assert.That(generatedFile.contents).Contains("[Definition(\"select \\\"active\\\" as status\")]");
        await Assert.That(generatedFile.contents).Contains("[View(\"active\\\"items\")]");

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

        await Assert.That(syntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_CachePolicy_EmitsDatabaseAndTableCacheAttributes()
    {
        var database = CreateDatabaseWithCachePolicy();
        var generatedFiles = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .ToList();

        var databaseFile = generatedFiles.Single(file => file.path == "CacheDb.cs");
        await Assert.That(databaseFile.contents).Contains("[UseCache]");
        await Assert.That(databaseFile.contents).Contains("[CacheLimit(CacheLimitType.Megabytes, 512)]");
        await Assert.That(databaseFile.contents).Contains("[CacheCleanup(CacheCleanupType.Minutes, 15)]");
        await Assert.That(databaseFile.contents).Contains("[IndexCache(IndexCacheType.MaxAmountRows, 2500)]");

        var modelFile = generatedFiles.Single(file => file.path == "CachedModel.cs");
        await Assert.That(modelFile.contents).Contains("[UseCache(false)]");
        await Assert.That(modelFile.contents).Contains("[CacheLimit(CacheLimitType.Rows, 25)]");
        await Assert.That(modelFile.contents).Contains("[IndexCache(IndexCacheType.All)]");
        await Assert.That(modelFile.contents).Contains("[Table(\"cached_items\")]");

        var databaseSyntaxTree = CSharpSyntaxTree.ParseText(databaseFile.contents);
        var databaseSyntaxErrors = databaseSyntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(databaseSyntaxErrors).IsEmpty();

        var modelSyntaxTree = CSharpSyntaxTree.ParseText(modelFile.contents);
        var modelSyntaxErrors = modelSyntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(modelSyntaxErrors).IsEmpty();
    }

    [Test]
    public async Task CreateModelFiles_PreservesPerFileNamespacesAndUsings()
    {
        var database = CreateDatabaseWithPerFileNamespacesAndUsings();
        var generatedFiles = new ModelFileFactory(new ModelFileFactoryOptions { UseFileScopedNamespaces = true })
            .CreateModelFiles(database)
            .ToList();

        var databaseFile = generatedFiles.Single(file => file.path == "PreserveDb.cs");
        await Assert.That(databaseFile.contents).Contains("using Existing.Database;");
        await Assert.That(databaseFile.contents).Contains("using Existing.Database.Helpers;");
        await Assert.That(databaseFile.contents).Contains("namespace Existing.Database;");

        var tableFile = generatedFiles.Single(file => file.path == "PreservedTable.cs");
        await Assert.That(tableFile.contents).Contains("using Existing.Tables;");
        await Assert.That(tableFile.contents).Contains("using Existing.Table.Helpers;");
        await Assert.That(tableFile.contents).Contains("namespace Existing.Tables;");

        var viewFile = generatedFiles.Single(file => file.path == "PreservedView.cs");
        await Assert.That(viewFile.contents).Contains("using Existing.Views;");
        await Assert.That(viewFile.contents).Contains("using Existing.View.Helpers;");
        await Assert.That(viewFile.contents).Contains("namespace Existing.Views;");
    }

    [Test]
    public async Task CreateModelFiles_PreservesFullyQualifiedValuePropertyType()
    {
        var database = CreateDatabaseWithCustomQualifiedPropertyType();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "ExternalStatusModel.cs");

        await Assert.That(generatedFile.contents).Contains("public abstract External.Namespace.DocumentStatusCode Status { get; }");
    }

    [Test]
    public async Task CreateModelFiles_DefaultLayout_PutsPrimaryKeysBeforeEarlierColumns()
    {
        var database = CreateDatabaseWithLayoutProperties();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "LayoutModel.cs");

        await AssertContainsInOrder(
            generatedFile.contents,
            "public abstract int ZuluId { get; }",
            "public abstract string Name { get; }",
            "public abstract string Alpha { get; }");
    }

    [Test]
    public async Task CreateModelFiles_AlphabeticalInlineLayout_DoesNotMovePrimaryKeys()
    {
        var database = CreateDatabaseWithLayoutProperties();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions
        {
            ModelLayout = new DataLinqModelLayoutConfig(
                DataLinqModelPropertyOrder.Alphabetical,
                DataLinqModelPrimaryKeyPlacement.Inline,
                DataLinqModelForeignKeyPlacement.Inline,
                DataLinqModelRelationPlacement.Bottom)
        })
            .CreateModelFiles(database)
            .Single(file => file.path == "LayoutModel.cs");

        await AssertContainsInOrder(
            generatedFile.contents,
            "public abstract string Alpha { get; }",
            "public abstract string Name { get; }",
            "public abstract int ZuluId { get; }");
    }

    [Test]
    public async Task CreateModelFiles_RelationPlacementTop_EmitsRelationsBeforeValueProperties()
    {
        var database = CreateDatabaseWithRelationLayout();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions
        {
            ModelLayout = new DataLinqModelLayoutConfig(
                DataLinqModelPropertyOrder.Column,
                DataLinqModelPrimaryKeyPlacement.Top,
                DataLinqModelForeignKeyPlacement.Inline,
                DataLinqModelRelationPlacement.Top)
        })
            .CreateModelFiles(database)
            .Single(file => file.path == "Order.cs");

        await AssertContainsInOrder(
            generatedFile.contents,
            "public abstract User Customer { get; }",
            "public abstract int OrderId { get; }",
            "public abstract int CustomerId { get; }",
            "public abstract decimal Amount { get; }");
    }

    [Test]
    public async Task CreateModelFiles_RelationPlacementWithForeignKey_EmitsRelationAfterForeignKeyProperty()
    {
        var database = CreateDatabaseWithRelationLayout();

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions
        {
            ModelLayout = new DataLinqModelLayoutConfig(
                DataLinqModelPropertyOrder.Column,
                DataLinqModelPrimaryKeyPlacement.Top,
                DataLinqModelForeignKeyPlacement.Inline,
                DataLinqModelRelationPlacement.WithForeignKey)
        })
            .CreateModelFiles(database)
            .Single(file => file.path == "Order.cs");

        await AssertContainsInOrder(
            generatedFile.contents,
            "public abstract int OrderId { get; }",
            "public abstract int CustomerId { get; }",
            "public abstract User Customer { get; }",
            "public abstract decimal Amount { get; }");
    }

    [Test]
    public async Task CreateModelFiles_ForeignKeyPlacementTop_EmitsForeignKeysBeforeEarlierOrdinaryColumns()
    {
        var database = CreateDatabaseWithRelationLayout(amountBeforeForeignKey: true);

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions
        {
            ModelLayout = new DataLinqModelLayoutConfig(
                DataLinqModelPropertyOrder.Column,
                DataLinqModelPrimaryKeyPlacement.Top,
                DataLinqModelForeignKeyPlacement.Top,
                DataLinqModelRelationPlacement.Bottom)
        })
            .CreateModelFiles(database)
            .Single(file => file.path == "Order.cs");

        await AssertContainsInOrder(
            generatedFile.contents,
            "public abstract int OrderId { get; }",
            "public abstract int CustomerId { get; }",
            "public abstract decimal Amount { get; }");
    }

    private static DatabaseDefinition CreateDatabaseWithDefaultValue(CsTypeDeclaration propertyType, object defaultValue)
    {
        var draft = new MetadataDatabaseDraft(
            "QuoteDb",
            new CsTypeDeclaration("QuoteDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "QuoteModels",
                    new MetadataModelDraft(new CsTypeDeclaration("QuoteModel", "TestNamespace", ModelCsType.Class))
                    {
                        ModelInstanceInterface = new CsTypeDeclaration("IQuoteModel", "TestNamespace", ModelCsType.Interface),
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "QuoteText",
                                propertyType,
                                new MetadataColumnDraft("quote_text")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "varchar", 10)]
                                })
                            {
                                Attributes = [new DefaultAttribute(defaultValue)]
                            }
                        ]
                    },
                    new MetadataTableDraft("quote_table"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithGuidDefault(DefaultAttribute defaultAttribute)
    {
        var draft = new MetadataDatabaseDraft(
            "QuoteDb",
            new CsTypeDeclaration("QuoteDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "QuoteModels",
                    new MetadataModelDraft(new CsTypeDeclaration("QuoteModel", "TestNamespace", ModelCsType.Class))
                    {
                        ModelInstanceInterface = new CsTypeDeclaration("IQuoteModel", "TestNamespace", ModelCsType.Interface),
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "QuoteText",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("quote_text")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "char", 36)]
                                })
                            {
                                Attributes =
                                [
                                    new GuidStorageAttribute(DatabaseType.MySQL, GuidStorageFormat.Text36),
                                    defaultAttribute
                                ]
                            }
                        ]
                    },
                    new MetadataTableDraft("quote_table"))
            ]
        };

        return Build(draft);
    }

    private sealed class CustomGuidDefaultAttribute(string value)
        : DefaultAttribute(Guid.ParseExact(value, "D"));

    private static DatabaseDefinition CreateDatabaseWithLayoutProperties()
    {
        var draft = new MetadataDatabaseDraft(
            "LayoutDb",
            new CsTypeDeclaration("LayoutDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "LayoutModels",
                    new MetadataModelDraft(new CsTypeDeclaration("LayoutModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            CreateValueProperty("Name", typeof(string), "name", "varchar", length: 255),
                            CreateValueProperty("ZuluId", typeof(int), "zulu_id", "int", primaryKey: true),
                            CreateValueProperty("Alpha", typeof(string), "alpha", "varchar", length: 255)
                        ]
                    },
                    new MetadataTableDraft("layout_model"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithRelationLayout(bool amountBeforeForeignKey = false)
    {
        var orderValueProperties = new List<MetadataValuePropertyDraft>
        {
            CreateValueProperty("OrderId", typeof(int), "order_id", "int", primaryKey: true)
        };

        var amountProperty = CreateValueProperty("Amount", typeof(decimal), "amount", "decimal");
        var customerIdProperty = new MetadataValuePropertyDraft(
            "CustomerId",
            new CsTypeDeclaration(typeof(int)),
            new MetadataColumnDraft("customer_id")
            {
                ForeignKey = true,
                DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "int")]
            })
        {
            Attributes =
            [
                new ForeignKeyAttribute("users", "user_id", "FK_Order_User"),
                new ColumnAttribute("customer_id")
            ]
        };

        if (amountBeforeForeignKey)
        {
            orderValueProperties.Add(amountProperty);
            orderValueProperties.Add(customerIdProperty);
        }
        else
        {
            orderValueProperties.Add(customerIdProperty);
            orderValueProperties.Add(amountProperty);
        }

        var draft = new MetadataDatabaseDraft(
            "OrderDb",
            new CsTypeDeclaration("OrderDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Users",
                    new MetadataModelDraft(new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            CreateValueProperty("UserId", typeof(int), "user_id", "int", primaryKey: true),
                            CreateValueProperty("Name", typeof(string), "name", "varchar", length: 255)
                        ]
                    },
                    new MetadataTableDraft("users")),
                new MetadataTableModelDraft(
                    "Orders",
                    new MetadataModelDraft(new CsTypeDeclaration("Order", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties = orderValueProperties
                    },
                    new MetadataTableDraft("orders"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithCustomQualifiedPropertyType()
    {
        var draft = new MetadataDatabaseDraft(
            "ExternalStatusDb",
            new CsTypeDeclaration("ExternalStatusDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "ExternalStatusModels",
                    new MetadataModelDraft(new CsTypeDeclaration("ExternalStatusModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            CreateValueProperty("Id", typeof(int), "id", "int", primaryKey: true),
                            new MetadataValuePropertyDraft(
                                "Status",
                                new CsTypeDeclaration("External.Namespace.DocumentStatusCode", "TestNamespace", ModelCsType.Class),
                                new MetadataColumnDraft("status")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "tinyint", 4UL)]
                                })
                        ]
                    },
                    new MetadataTableDraft("external_status"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithPerFileNamespacesAndUsings()
    {
        var draft = new MetadataDatabaseDraft(
            "PreserveDb",
            new CsTypeDeclaration("PreserveDb", "Existing.Database", ModelCsType.Class))
        {
            Usings = [new ModelUsing("Existing.Database"), new ModelUsing("Existing.Database.Helpers")],
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Tables",
                    new MetadataModelDraft(new CsTypeDeclaration("PreservedTable", "Existing.Tables", ModelCsType.Class))
                    {
                        Usings = [new ModelUsing("Existing.Tables"), new ModelUsing("Existing.Table.Helpers")],
                        ValueProperties =
                        [
                            CreateValueProperty("Id", typeof(int), "id", "int", primaryKey: true)
                        ]
                    },
                    new MetadataTableDraft("preserved_table")),
                new MetadataTableModelDraft(
                    "Views",
                    new MetadataModelDraft(new CsTypeDeclaration("PreservedView", "Existing.Views", ModelCsType.Class))
                    {
                        Usings = [new ModelUsing("Existing.Views"), new ModelUsing("Existing.View.Helpers")],
                        ValueProperties =
                        [
                            CreateValueProperty("Id", typeof(int), "id", "int")
                        ]
                    },
                    new MetadataTableDraft("preserved_view")
                    {
                        Type = TableType.View,
                        Definition = "select 1 as id"
                    })
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithCompositeIndex()
    {
        var draft = new MetadataDatabaseDraft(
            "IndexDb",
            new CsTypeDeclaration("IndexDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Accounts",
                    new MetadataModelDraft(new CsTypeDeclaration("Account", "TestNamespace", ModelCsType.Class))
                    {
                        Attributes =
                        [
                            new IndexAttribute("idx_account_year_number", IndexCharacteristic.Unique, IndexType.BTREE, "accounting_year", "account_number")
                        ],
                        ValueProperties =
                        [
                            CreateValueProperty("AccountingYear", typeof(int), "accounting_year", "int", primaryKey: true),
                            CreateValueProperty("AccountNumber", typeof(int), "account_number", "int"),
                            CreateValueProperty("Name", typeof(string), "name", "varchar", length: 255)
                        ]
                    },
                    new MetadataTableDraft("account"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithComments()
    {
        var draft = new MetadataDatabaseDraft(
            "CommentDb",
            new CsTypeDeclaration("CommentDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "CommentModels",
                    new MetadataModelDraft(new CsTypeDeclaration("CommentModel", "TestNamespace", ModelCsType.Class))
                    {
                        Attributes =
                        [
                            new CommentAttribute("Table comment with \"quotes\""),
                            new CheckAttribute(DatabaseType.MySQL, "CK_comment_model_name", "`name` <> ''")
                        ],
                        ValueProperties =
                        [
                            CreateValueProperty(
                                "Name",
                                typeof(string),
                                "name",
                                "varchar",
                                length: 255,
                                primaryKey: true,
                                attributes: [new CommentAttribute("Column comment with \"quotes\"")])
                        ]
                    },
                    new MetadataTableDraft("comment_model"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithGuidStorage()
    {
        var draft = new MetadataDatabaseDraft(
            "GuidStorageDb",
            new CsTypeDeclaration("GuidStorageDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "GuidStorageModels",
                    new MetadataModelDraft(new CsTypeDeclaration("GuidStorageModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes =
                                    [
                                        new DatabaseColumnType(DatabaseType.MySQL, "binary", 16),
                                        new DatabaseColumnType(DatabaseType.SQLite, "TEXT")
                                    ]
                                })
                            {
                                Attributes =
                                [
                                    new GuidStorageAttribute(GuidStorageFormat.Text36),
                                    new GuidStorageAttribute(
                                        DatabaseType.MySQL,
                                        GuidStorageFormat.Binary16Rfc4122)
                                ]
                            }
                        ]
                    },
                    new MetadataTableDraft("guid_storage_models"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithUuidGenerationDefaults()
    {
        var draft = new MetadataDatabaseDraft(
            "UuidDefaultDb",
            new CsTypeDeclaration("UuidDefaultDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "UuidDefaultModels",
                    new MetadataModelDraft(new CsTypeDeclaration("UuidDefaultModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true
                                }),
                            new MetadataValuePropertyDraft(
                                "Version4Id",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("version4_id"))
                            {
                                Attributes = [new DefaultNewUUIDAttribute(UUIDVersion.Version4)]
                            },
                            new MetadataValuePropertyDraft(
                                "Version7Id",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("version7_id"))
                            {
                                Attributes = [new DefaultNewUUIDAttribute(UUIDVersion.Version7)]
                            }
                        ]
                    },
                    new MetadataTableDraft("uuid_default_models"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithEscapedMetadataStrings()
    {
        var draft = new MetadataDatabaseDraft(
            "Escaped\"Db",
            new CsTypeDeclaration("EscapedDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "EscapedModels",
                    new MetadataModelDraft(new CsTypeDeclaration("EscapedModel", "TestNamespace", ModelCsType.Class))
                    {
                        Attributes =
                        [
                            new IndexAttribute("idx\"ship", IndexCharacteristic.Unique, IndexType.BTREE, "ship\"to", "2fa\\code")
                        ],
                        ValueProperties =
                        [
                            CreateValueProperty("ShipTo", typeof(string), "ship\"to", "var\"char", length: 255, primaryKey: true),
                            CreateValueProperty("TwoFactorCode", typeof(string), "2fa\\code", "varchar", length: 32)
                        ]
                    },
                    new MetadataTableDraft("order\"items"))
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithEscapedViewStrings()
    {
        var draft = new MetadataDatabaseDraft(
            "EscapedViewDb",
            new CsTypeDeclaration("EscapedViewDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "EscapedViewModels",
                    new MetadataModelDraft(new CsTypeDeclaration("EscapedViewModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            CreateValueProperty("Status", typeof(string), "status", "varchar", length: 16)
                        ]
                    },
                    new MetadataTableDraft("active\"items")
                    {
                        Type = TableType.View,
                        Definition = "select \"active\" as status"
                    })
            ]
        };

        return Build(draft);
    }

    private static DatabaseDefinition CreateDatabaseWithCachePolicy()
    {
        var draft = new MetadataDatabaseDraft(
            "CacheDb",
            new CsTypeDeclaration("CacheDb", "TestNamespace", ModelCsType.Class))
        {
            UseCache = true,
            CacheLimits = [(CacheLimitType.Megabytes, 512)],
            CacheCleanup = [(CacheCleanupType.Minutes, 15)],
            IndexCache = [(IndexCacheType.MaxAmountRows, 2500)],
            TableModels =
            [
                new MetadataTableModelDraft(
                    "CachedModels",
                    new MetadataModelDraft(new CsTypeDeclaration("CachedModel", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            CreateValueProperty("Id", typeof(int), "id", "int", primaryKey: true)
                        ]
                    },
                    new MetadataTableDraft("cached_items")
                    {
                        UseCache = false,
                        CacheLimits = [(CacheLimitType.Rows, 25)],
                        IndexCache = [(IndexCacheType.All, null)]
                    })
            ]
        };

        return Build(draft);
    }

    private static MetadataValuePropertyDraft CreateValueProperty(
        string propertyName,
        Type propertyType,
        string columnName,
        string dbType,
        int? length = null,
        bool primaryKey = false,
        Attribute[]? attributes = null)
    {
        return new MetadataValuePropertyDraft(
            propertyName,
            new CsTypeDeclaration(propertyType),
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, dbType, length is null ? null : (ulong)length.Value)]
            })
        {
            Attributes = attributes ?? []
        };
    }

    private static DatabaseDefinition Build(MetadataDatabaseDraft draft)
        => new MetadataDefinitionFactory().Build(draft).ValueOrException();

    private static async Task AssertContainsInOrder(string contents, params string[] markers)
    {
        var previousIndex = -1;
        foreach (var marker in markers)
        {
            var index = contents.IndexOf(marker, previousIndex + 1, StringComparison.Ordinal);
            await Assert.That(index).IsGreaterThan(previousIndex);
            previousIndex = index;
        }
    }
}
