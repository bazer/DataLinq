using System;
using System.Threading.Tasks;
using DataLinq.CLI;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit;

public class CliDiagnosticWriterTests
{
    [Test]
    public async Task FormatFailureText_PrefixesFailureWithErrorLabel()
    {
        var output = ConsoleDiagnosticWriter.FormatFailureText(
            DLOptionFailure.Fail(DLFailureType.InvalidModel, "Broken model"));

        await Assert.That(output).IsEqualTo($"Error: [InvalidModel] Broken model{Environment.NewLine}");
    }

    [Test]
    public async Task TryGetDiagnosticPrefix_DetectsWarningLabel()
    {
        var found = ConsoleDiagnosticWriter.TryGetDiagnosticPrefix(
            "Warning: Skipping foreign key 'FK'.",
            out var prefix,
            out var color);

        await Assert.That(found).IsTrue();
        await Assert.That(prefix).IsEqualTo("Warning:");
        await Assert.That(color).IsEqualTo(ConsoleColor.Yellow);
    }

    [Test]
    public async Task TryGetDiagnosticPrefix_DetectsErrorLabel()
    {
        var found = ConsoleDiagnosticWriter.TryGetDiagnosticPrefix(
            "Error: [InvalidModel] Broken model",
            out var prefix,
            out var color);

        await Assert.That(found).IsTrue();
        await Assert.That(prefix).IsEqualTo("Error:");
        await Assert.That(color).IsEqualTo(ConsoleColor.Red);
    }

    [Test]
    public async Task FormatFailureText_FlattensAggregateFailures()
    {
        var output = ConsoleDiagnosticWriter.FormatFailureText(
            DLOptionFailure.AggregateFail(
            [
                DLOptionFailure.Fail(DLFailureType.InvalidModel, "First problem"),
                DLOptionFailure.Fail(DLFailureType.FileNotFound, "Second problem")
            ]));

        await Assert.That(output).IsEqualTo(
            $"Error: [FileNotFound] Second problem{Environment.NewLine}" +
            $"Error: [InvalidModel] First problem{Environment.NewLine}");
    }

    [Test]
    public async Task FormatIssuesText_PrintsLineAndColumnWhenSourceTextIsAvailable()
    {
        var sourceLocation = new SourceLocation(
            new CsFileDeclaration("Account.cs"),
            new SourceTextSpan("line 1\n".Length, "public".Length));
        var issue = new DataLinqDiagnosticIssue(
            DataLinqDiagnosticSeverity.Error,
            DLFailureType.InvalidModel,
            "Broken property",
            sourceLocation);

        var output = ConsoleDiagnosticWriter.FormatIssuesText(
            [issue],
            _ => "line 1\npublic abstract string Name { get; }");

        await Assert.That(output).IsEqualTo(
            $"Error: Account.cs:2:1: [InvalidModel] Broken property{Environment.NewLine}");
    }
}
