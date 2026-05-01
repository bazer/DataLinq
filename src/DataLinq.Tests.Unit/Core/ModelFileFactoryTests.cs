using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    private static DatabaseDefinition CreateDatabaseWithDefaultValue(CsTypeDeclaration propertyType, object defaultValue)
    {
        var database = new DatabaseDefinition("QuoteDb", new CsTypeDeclaration("QuoteDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("QuoteModel", "TestNamespace", ModelCsType.Class));
        model.SetModelInstanceInterface(new CsTypeDeclaration("IQuoteModel", "TestNamespace", ModelCsType.Interface));

        var table = new TableDefinition("quote_table");
        var tableModel = new TableModel("QuoteModels", database, model, table);

        var property = new ValueProperty(
            "QuoteText",
            propertyType,
            model,
            [new DefaultAttribute(defaultValue)]);

        var column = new ColumnDefinition("quote_text", table);
        column.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "varchar", 10));
        column.SetValueProperty(property);
        table.SetColumns([column]);
        model.AddProperty(property);

        database.SetTableModels([tableModel]);

        return database;
    }

    private static DatabaseDefinition CreateDatabaseWithCompositeIndex()
    {
        var database = new DatabaseDefinition("IndexDb", new CsTypeDeclaration("IndexDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("Account", "TestNamespace", ModelCsType.Class));

        var table = new TableDefinition("account");
        var tableModel = new TableModel("Accounts", database, model, table);

        var accountingYearProperty = new ValueProperty("AccountingYear", new CsTypeDeclaration(typeof(int)), model, []);
        var accountNumberProperty = new ValueProperty("AccountNumber", new CsTypeDeclaration(typeof(int)), model, []);
        var nameProperty = new ValueProperty("Name", new CsTypeDeclaration(typeof(string)), model, []);

        var accountingYearColumn = new ColumnDefinition("accounting_year", table);
        accountingYearColumn.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "int"));
        accountingYearColumn.SetValueProperty(accountingYearProperty);

        var accountNumberColumn = new ColumnDefinition("account_number", table);
        accountNumberColumn.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "int"));
        accountNumberColumn.SetValueProperty(accountNumberProperty);

        var nameColumn = new ColumnDefinition("name", table);
        nameColumn.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "varchar", 255));
        nameColumn.SetValueProperty(nameProperty);

        table.SetColumns([accountingYearColumn, accountNumberColumn, nameColumn]);
        table.ColumnIndices.Add(new ColumnIndex("idx_account_year_number", IndexCharacteristic.Unique, IndexType.BTREE, [accountingYearColumn, accountNumberColumn]));

        model.AddProperty(accountingYearProperty);
        model.AddProperty(accountNumberProperty);
        model.AddProperty(nameProperty);
        database.SetTableModels([tableModel]);

        return database;
    }

    private static DatabaseDefinition CreateDatabaseWithComments()
    {
        var database = new DatabaseDefinition("CommentDb", new CsTypeDeclaration("CommentDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("CommentModel", "TestNamespace", ModelCsType.Class));
        model.AddAttribute(new CommentAttribute("Table comment with \"quotes\""));
        model.AddAttribute(new CheckAttribute(DatabaseType.MySQL, "CK_comment_model_name", "`name` <> ''"));

        var table = new TableDefinition("comment_model");
        var tableModel = new TableModel("CommentModels", database, model, table);

        var nameProperty = new ValueProperty("Name", new CsTypeDeclaration(typeof(string)), model, [new CommentAttribute("Column comment with \"quotes\"")]);
        var nameColumn = new ColumnDefinition("name", table);
        nameColumn.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "varchar", 255));
        nameColumn.SetValueProperty(nameProperty);

        table.SetColumns([nameColumn]);
        model.AddProperty(nameProperty);
        database.SetTableModels([tableModel]);

        return database;
    }
}
