using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ApiCompatibilityReporterTests
{
    [Test]
    public async Task Classifier_SeparatesBreaksFrameworkMismatchesAndAdditions()
    {
        var normal = Parse(
            Suppression("CP0002", "M:DataLinq.Removed()", baseline: true) +
            Suppression("CP0002", "F:DataLinq.CrossTarget", baseline: false));
        var strict = Parse(
            Suppression("CP0002", "M:DataLinq.Removed()", baseline: true) +
            Suppression("CP0002", "F:DataLinq.CrossTarget", baseline: false) +
            Suppression("CP0001", "T:DataLinq.Added", baseline: true));

        var findings = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq",
            null,
            ApiCompatibilityComparisonKind.PackageBaseline,
            normal,
            strict);

        await Assert.That(findings.Count).IsEqualTo(3);
        await Assert.That(findings.Count(static finding =>
            finding.ChangeKind == ApiCompatibilityChangeKind.CompatibilityBreak)).IsEqualTo(1);
        await Assert.That(findings.Count(static finding =>
            finding.ChangeKind == ApiCompatibilityChangeKind.CurrentPackageFrameworkMismatch)).IsEqualTo(1);
        await Assert.That(findings.Count(static finding =>
            finding.ChangeKind == ApiCompatibilityChangeKind.CompatibleApiChange)).IsEqualTo(1);
        await Assert.That(findings.Single(static finding =>
            finding.ChangeKind == ApiCompatibilityChangeKind.CompatibleApiChange).Severity)
            .IsEqualTo(ApiCompatibilityFindingSeverity.Review);
    }

    [Test]
    public async Task Classifier_TreatsParameterNameChangesAsSourceSensitiveBreaks()
    {
        var diagnostic = Parse(Suppression(
            "CP0017",
            "M:DataLinq.Example(System.String)",
            baseline: true));

        var finding = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq",
            "net10.0",
            ApiCompatibilityComparisonKind.PackageBaseline,
            diagnostic,
            diagnostic).Single();

        await Assert.That(finding.Code).IsEqualTo("source-sensitive-break");
        await Assert.That(finding.ChangeKind).IsEqualTo(ApiCompatibilityChangeKind.SourceSensitiveBreak);
        await Assert.That(finding.TargetFramework).IsEqualTo("net10.0");
    }

    [Test]
    public async Task Classifier_DeduplicatesOmittedAndExplicitFalseAcrossPasses()
    {
        var normal = Parse(
            """
            <Suppression>
              <DiagnosticId>CP0002</DiagnosticId>
              <Target>F:DataLinq.CrossTarget</Target>
              <Left>net8.dll</Left>
              <Right>net10.dll</Right>
            </Suppression>
            """);
        var strict = Parse(
            """
            <Suppression>
              <DiagnosticId>CP0002</DiagnosticId>
              <Target>F:DataLinq.CrossTarget</Target>
              <Left>net8.dll</Left>
              <Right>net10.dll</Right>
              <IsBaselineSuppression>false</IsBaselineSuppression>
            </Suppression>
            """);

        var findings = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq",
            null,
            ApiCompatibilityComparisonKind.PackageBaseline,
            normal,
            strict);

        await Assert.That(findings).HasSingleItem();
        await Assert.That(findings[0].ChangeKind)
            .IsEqualTo(ApiCompatibilityChangeKind.CurrentPackageFrameworkMismatch);
    }

    [Test]
    public async Task Classifier_UsesDirectAssemblyDirectionWhenBaselineMarkerIsAbsent()
    {
        var normal = Parse(
            """
            <Suppression>
              <DiagnosticId>CP0002</DiagnosticId>
              <Target>M:DataLinq.CLI.Removed()</Target>
              <Left>baseline.dll</Left>
              <Right>candidate.dll</Right>
            </Suppression>
            """);
        var strict = Parse(
            """
            <Suppression>
              <DiagnosticId>CP0002</DiagnosticId>
              <Target>M:DataLinq.CLI.Removed()</Target>
              <Left>baseline.dll</Left>
              <Right>candidate.dll</Right>
            </Suppression>
            <Suppression>
              <DiagnosticId>CP0001</DiagnosticId>
              <Target>T:DataLinq.CLI.Added</Target>
              <Left>baseline.dll</Left>
              <Right>candidate.dll</Right>
            </Suppression>
            """);

        var findings = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq.CLI",
            "net10.0",
            ApiCompatibilityComparisonKind.ToolAssemblyBaseline,
            normal,
            strict);

        await Assert.That(findings.Count).IsEqualTo(2);
        await Assert.That(findings.Single(static finding => finding.Code == "compatibility-break").Severity)
            .IsEqualTo(ApiCompatibilityFindingSeverity.Error);
        await Assert.That(findings.Single(static finding => finding.Code == "compatible-api-change").Severity)
            .IsEqualTo(ApiCompatibilityFindingSeverity.Review);
    }

    [Test]
    public async Task Classifier_CurrentFrameworkContextOverridesUnexpectedBaselineMarker()
    {
        var diagnostic = Parse(Suppression("CP0001", "T:DataLinq.CLI.FrameworkOnly", baseline: true));

        var finding = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq.CLI",
            "net8.0->net10.0",
            ApiCompatibilityComparisonKind.CurrentFramework,
            diagnostic,
            diagnostic).Single();

        await Assert.That(finding.Code).IsEqualTo("current-framework-mismatch");
        await Assert.That(finding.ChangeKind)
            .IsEqualTo(ApiCompatibilityChangeKind.CurrentPackageFrameworkMismatch);
    }

    [Test]
    public async Task Classifier_DirectAssemblyFingerprintIgnoresMachineSpecificPaths()
    {
        var first = Parse(
            SuppressionWithSides(
                "CP0001",
                "T:DataLinq.CLI.Added",
                @"D:\first\report\baseline.dll",
                @"D:\first\report\candidate.dll"));
        var second = Parse(
            SuppressionWithSides(
                "CP0001",
                "T:DataLinq.CLI.Added",
                "/home/agent/other/baseline.dll",
                "/home/agent/other/candidate.dll"));

        var firstFinding = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq.CLI",
            "net10.0",
            ApiCompatibilityComparisonKind.ToolAssemblyBaseline,
            [],
            first).Single();
        var secondFinding = ApiCompatibilityReporter.ClassifyDiagnostics(
            "DataLinq.CLI",
            "net10.0",
            ApiCompatibilityComparisonKind.ToolAssemblyBaseline,
            [],
            second).Single();

        await Assert.That(firstFinding.Fingerprint).IsEqualTo(secondFinding.Fingerprint);
        await Assert.That(firstFinding.Left).IsNotEqualTo(secondFinding.Left);
    }

    [Test]
    public async Task Markdown_StatesSupplementalSnapshotBoundary()
    {
        var state = new ApiCompatibilityRepositoryState("commit", "v0.9", false, "hash", true);
        var assembly = new ApiCompatibilityRunnerAssembly("runner", "1.0", "commit", true, "clean");
        var runner = new ApiCompatibilityRunnerEvidence(
            state,
            state,
            assembly,
            assembly,
            false,
            true,
            true,
            true,
            true,
            true,
            true);
        var summary = new ApiCompatibilityReportSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false);
        var report = new ApiCompatibilityReport(
            ApiCompatibilityReporter.SchemaVersion,
            DateTimeOffset.UnixEpoch,
            new ApiCompatibilityReportInvocation(
                "repo", "candidate", "0.9.0", "baseline", "0.8.0", "lock", ToolingProfile.Repo, [], []),
            "report",
            null,
            null,
            null,
            null,
            null,
            "10.0.302",
            runner,
            [],
            [],
            [],
            [],
            summary);

        var markdown = ApiCompatibilityReporter.ToMarkdown(report);

        await Assert.That(markdown)
            .Contains("managed binary/API shape")
            .And.Contains("does not prove behavioral compatibility")
            .And.Contains("generated-source compatibility");
    }

    [Test]
    public async Task Markdown_PreservesGenericDocIdsAndEncodesFindingText()
    {
        var state = new ApiCompatibilityRepositoryState("commit", "v0.9", false, "hash", true);
        var assembly = new ApiCompatibilityRunnerAssembly("runner", "1.0", "commit", true, "clean");
        var runner = new ApiCompatibilityRunnerEvidence(
            state,
            state,
            assembly,
            assembly,
            false,
            true,
            true,
            true,
            true,
            true,
            true);
        var finding = new ApiCompatibilityFinding(
            ApiCompatibilityFindingSeverity.Review,
            "compatible-api-change",
            "DataLinq",
            "net10.0",
            "Review <unsafe> & value.",
            ApiCompatibilityChangeKind.CompatibleApiChange,
            "CP0001",
            "T:DataLinq.Example`1",
            "baseline.dll",
            "candidate.dll",
            "fingerprint");
        var report = new ApiCompatibilityReport(
            ApiCompatibilityReporter.SchemaVersion,
            DateTimeOffset.UnixEpoch,
            new ApiCompatibilityReportInvocation(
                "repo", "candidate", "0.9.0", "baseline", "0.8.0", "lock", ToolingProfile.Repo, [], []),
            "report",
            null,
            null,
            null,
            null,
            null,
            "10.0.302",
            runner,
            [],
            [],
            [],
            [finding],
            new ApiCompatibilityReportSummary(0, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0, false, true));

        var markdown = ApiCompatibilityReporter.ToMarkdown(report);

        await Assert.That(markdown)
            .Contains("<code>T:DataLinq.Example`1</code>")
            .And.Contains("Review &lt;unsafe&gt; &amp; value.")
            .And.DoesNotContain("Example'1");
    }

    private static IReadOnlyList<ApiCompatSuppressionDiagnostic> Parse(string suppressions)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            $"<Suppressions>{suppressions}</Suppressions>"));
        return ApiCompatSuppressionParser.Parse(stream);
    }

    private static string Suppression(string id, string target, bool baseline) =>
        $$"""
        <Suppression>
          <DiagnosticId>{{id}}</DiagnosticId>
          <Target>{{target}}</Target>
          <Left>baseline.dll</Left>
          <Right>candidate.dll</Right>
          <IsBaselineSuppression>{{baseline.ToString().ToLowerInvariant()}}</IsBaselineSuppression>
        </Suppression>
        """;

    private static string SuppressionWithSides(string id, string target, string left, string right) =>
        $$"""
        <Suppression>
          <DiagnosticId>{{id}}</DiagnosticId>
          <Target>{{target}}</Target>
          <Left>{{left}}</Left>
          <Right>{{right}}</Right>
        </Suppression>
        """;
}
