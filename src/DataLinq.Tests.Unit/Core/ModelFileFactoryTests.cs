using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
}
