using System;
using System.Threading.Tasks;
using DataLinq.CLI;
using DataLinq.ErrorHandling;

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
}
