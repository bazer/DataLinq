using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ApiCompatToolRunnerTests
{
    [Test]
    public async Task Runner_VerifiesPinnedVersionAndParsesGeneratedDiagnostics()
    {
        using var fixture = new RunnerFixture();
        fixture.Process.Enqueue((_, _) => Result(0, "10.0.302+build-metadata\n"));
        fixture.Process.Enqueue((_, arguments) =>
        {
            WriteSuppression(arguments, "CP0002", baseline: true);
            return Result(0, string.Empty);
        });
        var runner = fixture.CreateRunner();

        var version = runner.VerifyTool();
        var comparison = runner.ComparePackages(
            "datalinq-baseline",
            System.IO.Path.Combine(fixture.Root, "baseline.nupkg"),
            System.IO.Path.Combine(fixture.Root, "candidate.nupkg"),
            strictBaseline: false);

        await Assert.That(version.Succeeded).IsTrue();
        await Assert.That(runner.ToolVersion).IsEqualTo("10.0.302+build-metadata");
        await Assert.That(comparison.Succeeded).IsTrue();
        await Assert.That(comparison.Diagnostics).HasSingleItem();
        await Assert.That(comparison.Diagnostics[0].DiagnosticId).IsEqualTo("CP0002");
        await Assert.That(string.Join(" ", comparison.Arguments.Take(4)))
            .IsEqualTo("tool run apicompat --");
        await Assert.That(comparison.Arguments).DoesNotContain("--tool-manifest");
        await Assert.That(comparison.Arguments).Contains("--generate-suppression-file");
        await Assert.That(comparison.Arguments).Contains("--run-api-compat");
        await Assert.That(comparison.Arguments).Contains("--enable-strict-mode-for-compatible-tfms");
        await Assert.That(comparison.Arguments).Contains("--enable-strict-mode-for-compatible-frameworks-in-package");
        await Assert.That(comparison.Arguments).DoesNotContain("--enable-strict-mode-for-baseline-validation");
        await Assert.That(File.Exists(comparison.StandardOutputPath)).IsTrue();
        await Assert.That(File.Exists(comparison.StandardErrorPath)).IsTrue();
    }

    [Test]
    public async Task Runner_TreatsSuccessfulMissingSuppressionAsZeroAndEmitsStrictFlag()
    {
        using var fixture = new RunnerFixture();
        fixture.Process.Enqueue((_, _) => Result(0, "10.0.302\n"));
        fixture.Process.Enqueue((_, _) => Result(0, string.Empty));
        var runner = fixture.CreateRunner();
        runner.VerifyTool();

        var comparison = runner.ComparePackages(
            "datalinq-strict",
            System.IO.Path.Combine(fixture.Root, "baseline.nupkg"),
            System.IO.Path.Combine(fixture.Root, "candidate.nupkg"),
            strictBaseline: true);

        await Assert.That(comparison.Succeeded).IsTrue();
        await Assert.That(comparison.Diagnostics).IsEmpty();
        await Assert.That(comparison.SuppressionPath).IsNull();
        await Assert.That(comparison.Arguments).Contains("--enable-strict-mode-for-baseline-validation");
    }

    [Test]
    public async Task Runner_RejectsWrongVersionAndAppendNeverEvidenceReuse()
    {
        using var fixture = new RunnerFixture();
        fixture.Process.Enqueue((_, _) => Result(0, "10.0.301\n"));
        var wrongRunner = fixture.CreateRunner();
        var wrong = wrongRunner.VerifyTool();

        await Assert.That(wrong.Succeeded).IsFalse();
        await Assert.That(wrong.Failure).Contains("expected pinned version '10.0.302'");

        using var freshFixture = new RunnerFixture();
        freshFixture.Process.Enqueue((_, _) => Result(0, "10.0.302\n"));
        var runner = freshFixture.CreateRunner();
        runner.VerifyTool();
        var exception = Capture<InvalidOperationException>(() => runner.VerifyTool());

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("append-never");
    }

    [Test]
    public async Task Runner_RejectsManifestOutsideCanonicalRepositoryLocation()
    {
        using var fixture = new RunnerFixture();
        var alternateManifest = System.IO.Path.Combine(fixture.Root, "alternate-tools.json");
        File.WriteAllText(alternateManifest, "{}", Encoding.UTF8);
        var runner = new ApiCompatToolRunner(
            DevToolPaths.Create(fixture.Root),
            ToolingProfile.Repo,
            alternateManifest,
            fixture.EvidencePath,
            fixture.Process);

        var execution = runner.VerifyTool();

        await Assert.That(execution.Succeeded).IsFalse();
        await Assert.That(execution.ExitCode).IsNull();
        await Assert.That(execution.Failure)
            .Contains(System.IO.Path.Combine(fixture.Root, ".config", "dotnet-tools.json"));
    }

    private static ExternalCommandResult Result(int exitCode, string stdout) =>
        new(exitCode, stdout, string.Empty) { Duration = TimeSpan.FromMilliseconds(25) };

    private static void WriteSuppression(
        IReadOnlyList<string> arguments,
        string diagnosticId,
        bool baseline)
    {
        var index = arguments.ToList().IndexOf("--suppression-output-file");
        if (index < 0 || index + 1 >= arguments.Count)
            throw new InvalidOperationException("Missing suppression output argument.");
        File.WriteAllText(
            arguments[index + 1],
            $$"""
            <Suppressions>
              <Suppression>
                <DiagnosticId>{{diagnosticId}}</DiagnosticId>
                <Target>T:DataLinq.Example</Target>
                <Left>baseline.dll</Left>
                <Right>candidate.dll</Right>
                <IsBaselineSuppression>{{baseline.ToString().ToLowerInvariant()}}</IsBaselineSuppression>
              </Suppression>
            </Suppressions>
            """,
            new UTF8Encoding(false));
    }

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

    private sealed class RunnerFixture : IDisposable
    {
        public RunnerFixture()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"datalinq-apicompat-runner-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            var configDirectory = System.IO.Path.Combine(Root, ".config");
            Directory.CreateDirectory(configDirectory);
            ManifestPath = System.IO.Path.Combine(configDirectory, "dotnet-tools.json");
            File.WriteAllText(ManifestPath, "{}", Encoding.UTF8);
            EvidencePath = System.IO.Path.Combine(Root, "evidence");
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string EvidencePath { get; }

        public FakeProcessRunner Process { get; } = new();

        public ApiCompatToolRunner CreateRunner() =>
            new(
                DevToolPaths.Create(Root),
                ToolingProfile.Repo,
                ManifestPath,
                EvidencePath,
                Process);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeProcessRunner : IApiCompatProcessRunner
    {
        private readonly Queue<Func<string, IReadOnlyList<string>, ExternalCommandResult>> results = new();

        public void Enqueue(Func<string, IReadOnlyList<string>, ExternalCommandResult> result) =>
            results.Enqueue(result);

        public ExternalCommandResult Execute(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environmentVariables)
        {
            if (results.Count == 0)
                throw new InvalidOperationException("No fake ApiCompat result was queued.");
            return results.Dequeue()(fileName, arguments);
        }
    }
}
