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
}
