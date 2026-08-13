using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ApiCompatSuppressionParserTests
{
    [Test]
    public async Task Parse_OfficialShapeReturnsDeterministicallySortedDiagnostics()
    {
        var diagnostics = Parse(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <!-- https://learn.microsoft.com/dotnet/fundamentals/package-validation/diagnostic-ids -->
            <Suppressions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <Suppression>
                <DiagnosticId> CP0002 </DiagnosticId>
                <Target> F:DataLinq.Example.Z </Target>
                <Left> lib/net9.0/DataLinq.dll </Left>
                <Right> lib/net10.0/DataLinq.dll </Right>
              </Suppression>
              <Suppression>
                <DiagnosticId>CP0001</DiagnosticId>
                <Target>T:DataLinq.Example.B</Target>
                <Left>lib/net8.0/DataLinq.dll</Left>
                <Right>lib/net9.0/DataLinq.dll</Right>
                <IsBaselineSuppression>true</IsBaselineSuppression>
              </Suppression>
              <Suppression>
                <DiagnosticId>CP0001</DiagnosticId>
                <Target>T:DataLinq.Example.A</Target>
                <Left>lib/net8.0/DataLinq.dll</Left>
                <Right>lib/net9.0/DataLinq.dll</Right>
                <IsBaselineSuppression>false</IsBaselineSuppression>
              </Suppression>
            </Suppressions>
            """);

        await Assert.That(diagnostics.Count).IsEqualTo(3);
        await Assert.That(string.Join(
                "|",
                diagnostics.Select(static diagnostic =>
                    $"{diagnostic.DiagnosticId},{diagnostic.Target},{diagnostic.IsBaselineSuppression?.ToString() ?? "null"}")))
            .IsEqualTo(
                "CP0001,T:DataLinq.Example.A,False|" +
                "CP0001,T:DataLinq.Example.B,True|" +
                "CP0002,F:DataLinq.Example.Z,null");
        await Assert.That(diagnostics[2].Left).IsEqualTo("lib/net9.0/DataLinq.dll");
        await Assert.That(diagnostics[2].Right).IsEqualTo("lib/net10.0/DataLinq.dll");
        await Assert.That(diagnostics.All(static diagnostic =>
                diagnostic.Fingerprint.Length == 64 &&
                diagnostic.Fingerprint.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            .IsTrue();
    }

    [Test]
    public async Task Parse_FingerprintUsesCanonicalFieldsRatherThanXmlOrderOrFormatting()
    {
        var first = Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0002</DiagnosticId>
                  <Target>M:DataLinq.Example.Run()</Target>
                  <Left>lib/net8.0/DataLinq.dll</Left>
                  <Right>lib/net9.0/DataLinq.dll</Right>
                  <IsBaselineSuppression>true</IsBaselineSuppression>
                </Suppression>
                """))[0];
        var reordered = Parse(
            Document(
                """
                <Suppression>
                  <Right> lib/net9.0/DataLinq.dll </Right>
                  <IsBaselineSuppression> true </IsBaselineSuppression>
                  <Target> M:DataLinq.Example.Run() </Target>
                  <DiagnosticId> CP0002 </DiagnosticId>
                  <Left> lib/net8.0/DataLinq.dll </Left>
                </Suppression>
                """))[0];

        await Assert.That(reordered).IsEqualTo(first);
        await Assert.That(reordered.Fingerprint).IsEqualTo(first.Fingerprint);
        await Assert.That(first.Fingerprint)
            .IsEqualTo("81ed8be5a12cb490ef88b003bf55fa2949bfd9bf3592373e059e1ceb7610b957");
    }

    [Test]
    public async Task Parse_EmptyRootReturnsNoDiagnostics()
    {
        // ApiCompat 10.0.302 creates no output file when a same-assembly comparison has no
        // differences. If a caller deliberately retains the equivalent empty root, it is empty data.
        var diagnostics = Parse(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <!-- https://learn.microsoft.com/dotnet/fundamentals/package-validation/diagnostic-ids -->
            <Suppressions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" />
            """);

        await Assert.That(diagnostics.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_EmptyFileIsInvalidRatherThanAnEmptyReport()
    {
        var exception = Capture<InvalidDataException>(() => Parse(string.Empty, "empty.xml"));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("empty.xml")
            .And.Contains("Root element is missing")
            .And.Contains("<Suppressions>");
        await Assert.That(exception.InnerException).IsTypeOf<XmlException>();
    }

    [Test]
    public async Task Parse_ProhibitsDtdAndExternalEntityDeclarations()
    {
        var exception = Capture<InvalidDataException>(() => Parse(
            """
            <!DOCTYPE Suppressions [<!ENTITY diagnosticId "CP0001">]>
            <Suppressions>
              <Suppression>
                <DiagnosticId>&diagnosticId;</DiagnosticId>
                <Target>T:DataLinq.Example</Target>
                <Left>left.dll</Left>
                <Right>right.dll</Right>
              </Suppression>
            </Suppressions>
            """,
            "dtd.xml"));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("dtd.xml")
            .And.Contains("DTD is prohibited");
        await Assert.That(exception.InnerException).IsTypeOf<XmlException>();
    }

    [Test]
    [Arguments("<suppressions />", "Root element must be exact unqualified <Suppressions>")]
    [Arguments("<Suppressions xmlns=\"urn:not-apicompat\" />", "Root element must be exact unqualified <Suppressions>")]
    public async Task Parse_RequiresExactUnqualifiedRoot(string xml, string expectedMessage)
    {
        var exception = Capture<InvalidDataException>(() => Parse(xml));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains(expectedMessage);
    }

    [Test]
    public async Task Parse_RequiresEveryNonblankDiagnosticField()
    {
        var missingRight = Capture<InvalidDataException>(() => Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Left>left.dll</Left>
                </Suppression>
                """)));
        var blankTarget = Capture<InvalidDataException>(() => Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>   </Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                </Suppression>
                """)));

        await Assert.That(missingRight).IsNotNull();
        await Assert.That(missingRight!.Message).Contains("missing required <Right>");
        await Assert.That(blankTarget).IsNotNull();
        await Assert.That(blankTarget!.Message).Contains("<Target> must not be blank");
    }

    [Test]
    [Arguments("True")]
    [Arguments("FALSE")]
    [Arguments("1")]
    [Arguments("")]
    public async Task Parse_RejectsNonCanonicalBaselineBoolean(string value)
    {
        var exception = Capture<InvalidDataException>(() => Parse(
            Document(
                $$"""
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                  <IsBaselineSuppression>{{value}}</IsBaselineSuppression>
                </Suppression>
                """)));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("<IsBaselineSuppression>")
            .And.Contains("exact lowercase 'true' or 'false'");
    }

    [Test]
    public async Task Parse_RejectsDuplicateDiagnosticIdentityWhenOmittedAndFalseBaselineFlagsAreEquivalent()
    {
        var exception = Capture<InvalidDataException>(() => Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                </Suppression>
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                  <IsBaselineSuppression>false</IsBaselineSuppression>
                </Suppression>
                """)));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("Suppression 2 duplicates diagnostic 1")
            .And.Contains("a diagnostic may appear only once");
    }

    [Test]
    public async Task Parse_AllowsSameDiagnosticIdentityForCurrentAndBaselineComparisons()
    {
        var diagnostics = Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                  <IsBaselineSuppression>false</IsBaselineSuppression>
                </Suppression>
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                  <IsBaselineSuppression>true</IsBaselineSuppression>
                </Suppression>
                """));

        await Assert.That(diagnostics.Count).IsEqualTo(2);
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.IsBaselineSuppression))
            .IsEquivalentTo(new bool?[] { false, true });
    }

    [Test]
    [Arguments("<Suppressions unexpected=\"value\" />", "unsupported attribute 'unexpected'")]
    [Arguments("<Suppressions><Unexpected /></Suppressions>", "may contain only <Suppression> elements")]
    [Arguments("<Suppressions>unexpected text</Suppressions>", "non-whitespace text")]
    [Arguments("<Suppressions><?unexpected value?></Suppressions>", "processing instruction 'unexpected'")]
    [Arguments("<Suppressions><Suppression unexpected=\"value\" /></Suppressions>", "Suppression 1 has unsupported attribute")]
    [Arguments("<Suppressions><Suppression><Unexpected /></Suppression></Suppressions>", "contains unknown field <Unexpected>")]
    public async Task Parse_RejectsUnknownStructure(string xml, string expectedMessage)
    {
        var exception = Capture<InvalidDataException>(() => Parse(xml));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains(expectedMessage);
    }

    [Test]
    public async Task Parse_RejectsDuplicateOrComplexFields()
    {
        var duplicateField = Capture<InvalidDataException>(() => Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target>T:DataLinq.Example</Target>
                  <Target>T:DataLinq.Other</Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                </Suppression>
                """)));
        var complexField = Capture<InvalidDataException>(() => Parse(
            Document(
                """
                <Suppression>
                  <DiagnosticId>CP0001</DiagnosticId>
                  <Target><Nested /></Target>
                  <Left>left.dll</Left>
                  <Right>right.dll</Right>
                </Suppression>
                """)));

        await Assert.That(duplicateField).IsNotNull();
        await Assert.That(duplicateField!.Message).Contains("duplicate field <Target>");
        await Assert.That(complexField).IsNotNull();
        await Assert.That(complexField!.Message).Contains("<Target> must contain text only and no attributes");
    }

    [Test]
    public async Task Parse_MalformedXmlIncludesSourceAndLineContext()
    {
        var exception = Capture<InvalidDataException>(() => Parse(
            "<Suppressions><Suppression></Suppressions>",
            "broken-suppressions.xml"));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message)
            .Contains("broken-suppressions.xml")
            .And.Contains("does not match")
            .And.Contains("<Suppressions>");
        await Assert.That(exception.InnerException).IsTypeOf<XmlException>();
    }

    private static IReadOnlyList<ApiCompatSuppressionDiagnostic> Parse(
        string xml,
        string sourceName = "fixture-suppressions.xml")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return ApiCompatSuppressionParser.Parse(stream, sourceName);
    }

    private static string Document(string suppressions) =>
        $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <!-- https://learn.microsoft.com/dotnet/fundamentals/package-validation/diagnostic-ids -->
        <Suppressions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          {{suppressions}}
        </Suppressions>
        """;

    private static TException? Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }
}
