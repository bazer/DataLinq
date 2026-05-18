using System;
using System.IO;
using System.Threading.Tasks;
using DataLinq.CLI;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit;

public class CliDiagnosticWriterTests
{
    [Test]
    public async Task FormatFailureText_PrefixesFailureWithErrorLabel()
    {
        var output = ConsoleDiagnosticWriter.FormatFailureText(
            DLOptionFailure.Fail(DLFailureType.InvalidModel, "Broken model"));

        await Assert.That(output).IsEqualTo($"error InvalidModel: Broken model{Environment.NewLine}");
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
    [NotInParallel]
    public async Task WriteLogLine_RendersLegacyWarningPrefixAsStderrDiagnostic()
    {
        var (stdout, stderr) = CaptureConsole(() =>
            ConsoleDiagnosticWriter.WriteLogLine("Warning: Skipping foreign key 'FK'."));

        await Assert.That(stdout).IsEmpty();
        await Assert.That(stderr).IsEqualTo($"warning: Skipping foreign key 'FK'.{Environment.NewLine}");
    }

    [Test]
    [NotInParallel]
    public async Task WriteError_WritesToStderr()
    {
        var (stdout, stderr) = CaptureConsole(() =>
            ConsoleDiagnosticWriter.WriteError("InvalidArgument", "Bad option."));

        await Assert.That(stdout).IsEmpty();
        await Assert.That(stderr).IsEqualTo($"error InvalidArgument: Bad option.{Environment.NewLine}");
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
            $"error FileNotFound: Second problem{Environment.NewLine}" +
            $"error InvalidModel: First problem{Environment.NewLine}");
    }

    [Test]
    public async Task FormatFailureText_OmitsUnspecifiedFailureCode()
    {
        var output = ConsoleDiagnosticWriter.FormatFailureText(
            DLOptionFailure.Fail("Couldn't find database with name 'Foo'."));

        await Assert.That(output).IsEqualTo(
            $"error: Couldn't find database with name 'Foo'.{Environment.NewLine}");
    }

    [Test]
    public async Task FormatFailureText_RedactsRegisteredSecrets()
    {
        var originalRedactor = ConsoleDiagnosticWriter.Redactor;
        try
        {
            var redactor = new SecretRedactor();
            redactor.Register("super-secret");
            ConsoleDiagnosticWriter.Redactor = redactor;

            var output = ConsoleDiagnosticWriter.FormatFailureText(
                DLOptionFailure.Fail(DLFailureType.InvalidModel, "Connection failed with super-secret"));

            await Assert.That(output).DoesNotContain("super-secret");
            await Assert.That(output).Contains("********");
        }
        finally
        {
            ConsoleDiagnosticWriter.Redactor = originalRedactor;
        }
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
            $"Account.cs:2:1: error InvalidModel: Broken property{Environment.NewLine}");
    }

    [Test]
    public async Task FormatIssuesText_UsesWarningPrefixForWarningIssues()
    {
        var issue = new DataLinqDiagnosticIssue(
            DataLinqDiagnosticSeverity.Warning,
            DLFailureType.InvalidModel,
            "Suspicious property");

        var output = ConsoleDiagnosticWriter.FormatIssuesText([issue]);

        await Assert.That(output).IsEqualTo(
            $"warning InvalidModel: Suspicious property{Environment.NewLine}");
    }

    [Test]
    public async Task FormatIssuesText_PrintsIndentedContextLines()
    {
        var issue = new DataLinqDiagnosticIssue(
            DataLinqDiagnosticSeverity.Error,
            DLFailureType.InvalidModel,
            "Broken property",
            contextMessages:
            [
                "Parsing properties for Employee",
                "Parsing models from Models/Employee.cs"
            ]);

        var output = ConsoleDiagnosticWriter.FormatIssuesText([issue]);

        await Assert.That(output).IsEqualTo(
            $"error InvalidModel: Broken property{Environment.NewLine}" +
            $"  context: Parsing properties for Employee{Environment.NewLine}" +
            $"  context: Parsing models from Models/Employee.cs{Environment.NewLine}");
    }

    [Test]
    public async Task FormatValidationDifferenceText_UsesDiagnosticStyleWithoutSafetyBrackets()
    {
        var difference = new SchemaDifference(
            SchemaDifferenceKind.ColumnTypeMismatch,
            SchemaDifferenceSeverity.Error,
            SchemaDifferenceSafety.Ambiguous,
            "employee.birth_date",
            "Model type 'date' does not match database type 'datetime'.");

        var output = Program.FormatValidationDifferenceText(difference);

        await Assert.That(output).IsEqualTo(
            $"error ColumnTypeMismatch: employee.birth_date{Environment.NewLine}" +
            $"  safety: Ambiguous{Environment.NewLine}" +
            "  Model type 'date' does not match database type 'datetime'.");
        await Assert.That(output).DoesNotContain("[");
        await Assert.That(output).DoesNotContain("]");
    }

    private static (string Stdout, string Stderr) CaptureConsole(Action action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
#pragma warning disable TUnit0055
            Console.SetOut(stdout);
            Console.SetError(stderr);
#pragma warning restore TUnit0055
            action();
            return (stdout.ToString(), stderr.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
            Console.SetError(originalError);
#pragma warning restore TUnit0055
        }
    }
}
