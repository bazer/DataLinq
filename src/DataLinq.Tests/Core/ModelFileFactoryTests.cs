using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataLinq.Tests.Core;

public class ModelFileFactoryTests
{
    [Fact]
    public void CreateModelFiles_DefaultStringContainingDoubleQuotes_EscapesValue()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(string)), "'\"\"'");

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        Assert.Contains("[Default(\"'\\\"\\\"'\")]", generatedFile.contents);

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        Assert.Empty(syntaxErrors);
    }

    [Fact]
    public void CreateModelFiles_DefaultCharDoubleQuote_EscapesValue()
    {
        var database = CreateDatabaseWithDefaultValue(new CsTypeDeclaration(typeof(char)), '"');

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "QuoteModel.cs");

        Assert.Contains("[Default('", generatedFile.contents);
        Assert.DoesNotContain("[Default(\"", generatedFile.contents);

        var syntaxTree = CSharpSyntaxTree.ParseText(generatedFile.contents);
        var syntaxErrors = syntaxTree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        Assert.Empty(syntaxErrors);
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
