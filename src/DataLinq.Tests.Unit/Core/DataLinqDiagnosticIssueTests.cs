using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Tests.Unit.Core;

public class DataLinqDiagnosticIssueTests
{
    [Test]
    public async Task SourceLocationFormatter_Format_WithSourceText_UsesOneBasedLineAndColumn()
    {
        const string sourceText = "first\r\nsecond value\r\nthird";
        var start = sourceText.IndexOf("value");
        var location = new SourceLocation(
            new CsFileDeclaration("Models/Example.cs"),
            new SourceTextSpan(start, "value".Length));

        var formatted = SourceLocationFormatter.Format(location, sourceText);

        await Assert.That(formatted).IsEqualTo("Models/Example.cs:2:8");
    }

    [Test]
    public async Task SourceLocationFormatter_Format_WithoutSourceText_FallsBackToFile()
    {
        var location = new SourceLocation(
            new CsFileDeclaration("Models/Example.cs"),
            new SourceTextSpan(10, 5));

        var formatted = SourceLocationFormatter.Format(location);

        await Assert.That(formatted).IsEqualTo("Models/Example.cs");
    }

    [Test]
    public async Task FromFailure_FlattensAggregateFailuresWithContextAndDeterministicLocationOrdering()
    {
        const string sourceText = "line1\nline2\nline3";
        var laterFailure = DLOptionFailure.Fail(
            DLFailureType.InvalidModel,
            "Later failure",
            new SourceLocation(new CsFileDeclaration("Models/Example.cs"), new SourceTextSpan(sourceText.IndexOf("line3"), 5)));
        var earlierFailure = DLOptionFailure.Fail(
            DLFailureType.InvalidArgument,
            "Earlier failure",
            new SourceLocation(new CsFileDeclaration("Models/Example.cs"), new SourceTextSpan(sourceText.IndexOf("line1"), 5)));
        var aggregate = DLOptionFailure.Fail("Parsing properties", [laterFailure, earlierFailure]);

        var issues = DataLinqDiagnosticIssue.FromFailure(aggregate);

        await Assert.That(issues.Count).IsEqualTo(2);
        await Assert.That(issues[0].Message).IsEqualTo("Earlier failure");
        await Assert.That(issues[0].FailureType).IsEqualTo(DLFailureType.InvalidArgument);
        await Assert.That(issues[0].ContextMessages).IsEquivalentTo(["Parsing properties"]);
        await Assert.That(issues[0].FormatLocation(sourceText)).IsEqualTo("Models/Example.cs:1:1");
        await Assert.That(issues[1].Message).IsEqualTo("Later failure");
        await Assert.That(issues[1].FormatLocation(sourceText)).IsEqualTo("Models/Example.cs:3:1");
    }

    [Test]
    public async Task Fail_WithColumnDefinition_UsesColumnAttributeSourceLocationAndObjectPath()
    {
        const string sourceText = """
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace TestNamespace;

[Database("test_db")]
public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UserModel> Users { get; }
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<UserModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }

    [Column("name")]
    public abstract string Name { get; }
}
""";

        var database = ParseDatabase(sourceText);
        var column = database.TableModels
            .Single(tableModel => tableModel.Table.DbName == "users")
            .Table.Columns
            .Single(column => column.DbName == "name");

        var failure = DLOptionFailure.Fail(DLFailureType.InvalidModel, "Bad column", column);
        var issues = DataLinqDiagnosticIssue.FromFailure(failure);

        await Assert.That(failure.SourceLocation.HasValue).IsTrue();
        await Assert.That(failure.SourceLocation!.Value.Span!.Value.Start).IsEqualTo(sourceText.IndexOf("Column(\"name\")"));
        await Assert.That(SourceLocationFormatter.Format(failure.SourceLocation.Value, sourceText)).IsEqualTo("Models/UserModel.cs:23:6");
        await Assert.That(issues.Single().ObjectPath).IsEqualTo("database:test_db.table:users.column:name");
    }

    [Test]
    public async Task Fail_WithTableDefinition_UsesTableAttributeSourceLocation()
    {
        const string sourceText = """
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace TestNamespace;

[Database("test_db")]
public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UserModel> Users { get; }
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<UserModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
}
""";

        var database = ParseDatabase(sourceText);
        var table = database.TableModels.Single().Table;

        var failure = DLOptionFailure.Fail(DLFailureType.InvalidModel, "Bad table", table);

        await Assert.That(failure.SourceLocation.HasValue).IsTrue();
        await Assert.That(failure.SourceLocation!.Value.Span!.Value.Start).IsEqualTo(sourceText.IndexOf("Table(\"users\")"));
        await Assert.That(SourceLocationFormatter.Format(failure.SourceLocation.Value, sourceText)).IsEqualTo("Models/UserModel.cs:16:2");
    }

    private static DatabaseDefinition ParseDatabase(string sourceText)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: "Models/UserModel.cs");
        var root = syntaxTree.GetCompilationUnitRoot();
        var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

        return factory.ReadSyntaxTrees(declarations).Single().Value;
    }
}
