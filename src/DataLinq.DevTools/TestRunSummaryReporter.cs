using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DataLinq.DevTools;

public static class TestRunSummaryReporter
{
    public const string SchemaVersion = "v0.9.testing-run-summary.v2";

    private const string RepositoryBuildStateMetadataName = "DataLinqRepositoryBuildState";
    private const string CleanRepositoryBuildState = "clean";
    private const string ExpectedEntryAssemblyName = "DataLinq.Testing.CLI";
    private const string ExpectedDevToolsAssemblyName = "DataLinq.DevTools";
    private const int MaximumFailureMessageLength = 2_000;

    private static readonly Regex CredentialAssignmentPattern = new(
        """(?<name>\b(?:MYSQL_ROOT_PASSWORD|MYSQL_PASSWORD|MARIADB_ROOT_PASSWORD|MARIADB_PASSWORD|DATALINQ_TEST_DB_ADMIN_PASSWORD|DATALINQ_TEST_DB_APP_PASSWORD))\s*=\s*(?:"[^"]*"|'[^']*'|[^\s,;\]\}]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, ReleaseSuiteContract> ReleaseSuites =
        new Dictionary<string, ReleaseSuiteContract>(StringComparer.OrdinalIgnoreCase)
        {
            ["generators"] = new(Path.Combine("src", "DataLinq.Generators.Tests", "DataLinq.Generators.Tests.csproj"), false, false),
            ["unit"] = new(Path.Combine("src", "DataLinq.Tests.Unit", "DataLinq.Tests.Unit.csproj"), false, false),
            ["memory"] = new(Path.Combine("src", "DataLinq.Tests.Memory", "DataLinq.Tests.Memory.csproj"), false, false),
            ["compliance"] = new(Path.Combine("src", "DataLinq.Tests.Compliance", "DataLinq.Tests.Compliance.csproj"), true, true),
            ["mysql"] = new(Path.Combine("src", "DataLinq.Tests.MySql", "DataLinq.Tests.MySql.csproj"), true, false)
        };

    private static readonly IReadOnlyDictionary<string, ReleaseTargetContract> ReleaseTargets =
        new Dictionary<string, ReleaseTargetContract>(StringComparer.OrdinalIgnoreCase)
        {
            ["sqlite-file"] = new(UsesPodman: false, HostPort: null),
            ["sqlite-memory"] = new(UsesPodman: false, HostPort: null),
            ["mysql-8.4"] = new(UsesPodman: true, HostPort: 13307),
            ["mariadb-10.11"] = new(UsesPodman: true, HostPort: 13310),
            ["mariadb-11.4"] = new(UsesPodman: true, HostPort: 13309),
            ["mariadb-11.8"] = new(UsesPodman: true, HostPort: 13308)
        };

    private static readonly TestRunSummaryRepositoryState UnavailableRepositoryState = new(
        Captured: false,
        Commit: "unknown",
        Branch: "unknown",
        Dirty: true,
        StatusSha256: "unknown");

    private static readonly TestRunSummaryRunnerAssembly UnknownRunnerAssembly = new(
        Name: "unknown",
        InformationalVersion: "unknown",
        RepositoryCommit: "unknown",
        RepositoryCommitCaptured: false,
        RepositoryBuildState: "unknown");

    public static TestRunSummaryReport Create(TestRunSummaryReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Invocation);
        ArgumentNullException.ThrowIfNull(input.RepositoryStart);
        ArgumentNullException.ThrowIfNull(input.RepositoryEnd);
        ArgumentNullException.ThrowIfNull(input.EntryAssembly);
        ArgumentNullException.ThrowIfNull(input.DevToolsAssembly);
        ArgumentNullException.ThrowIfNull(input.ExpectedResults);
        ArgumentNullException.ThrowIfNull(input.Builds);
        ArgumentNullException.ThrowIfNull(input.Results);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RunId);

        if (input.CompletedAtUtc < input.StartedAtUtc)
            throw new ArgumentException("The test summary completion timestamp cannot precede its start timestamp.", nameof(input));

        ValidateInvocation(input.Invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ReportPath);

        var reportPath = Path.GetFullPath(input.ReportPath);
        var invocation = Normalize(input.Invocation);
        var expectedResults = input.ExpectedResults.Select(Normalize).ToArray();
        var builds = input.Builds.Select(Normalize).ToArray();
        var results = input.Results.Select(Normalize).ToArray();
        var countsComplete = HasCompleteCounts(input, results);
        var invocationComplete = IsInvocationComplete(invocation, expectedResults, builds, results, countsComplete);
        var artifactsComplete = HasCompleteArtifacts(invocation.RepositoryRoot, reportPath, builds, results);
        var outcome = DetermineOutcome(input, results, invocationComplete, countsComplete, artifactsComplete);
        var fullMatrixInvocation = HasCanonicalReleaseScope(invocation);
        var hasPerTargetProviderTotals = HasPerTargetProviderCoverage(invocation, expectedResults);
        var stateChanged = HasRepositoryStateChanged(input.RepositoryStart, input.RepositoryEnd);
        var assembliesMatch = AssembliesMatchCheckout(
            input.RepositoryStart,
            input.RepositoryEnd,
            input.EntryAssembly,
            input.DevToolsAssembly);
        var assembliesClean = IsCleanAssembly(input.EntryAssembly, ExpectedEntryAssemblyName) &&
                              IsCleanAssembly(input.DevToolsAssembly, ExpectedDevToolsAssemblyName);
        var repositoryClean = input.RepositoryStart.Captured &&
                              input.RepositoryEnd.Captured &&
                              !input.RepositoryStart.Dirty &&
                              !input.RepositoryEnd.Dirty &&
                              !stateChanged;
        var validForEvidence = outcome == TestRunSummaryOutcome.Passed &&
                               invocationComplete &&
                               artifactsComplete &&
                               fullMatrixInvocation &&
                               hasPerTargetProviderTotals &&
                               repositoryClean &&
                               assembliesMatch &&
                               assembliesClean;
        var runnerEvidence = new TestRunSummaryRunnerEvidence(
            input.RepositoryStart,
            input.RepositoryEnd,
            input.EntryAssembly,
            input.DevToolsAssembly,
            stateChanged,
            assembliesMatch,
            assembliesClean,
            validForEvidence);
        var timings = new TestRunSummaryTimingBreakdown(
            BuildProcessSeconds: RoundSeconds(builds.Sum(static build => build.DurationSeconds)),
            InfrastructureSetupSeconds: RoundSeconds(results.Sum(static result => result.InfrastructureSetupDurationSeconds)),
            TestHostProcessSeconds: RoundSeconds(results.Sum(static result => result.DurationSeconds)),
            TestBodySeconds: RoundSeconds(results.Sum(static result => result.Performance.TotalTestDurationSeconds)),
            TeardownSeconds: RoundSeconds(input.TeardownDurationSeconds));
        var runtimeEnvironment = new TestRunSummaryRuntimeEnvironment(
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount);
        var artifactPaths = new[] { reportPath }
            .Concat(builds.Select(static build => build.LogPath))
            .Concat(results.SelectMany(static result => result.ArtifactPaths))
            .Distinct(PathComparer)
            .ToArray();

        return new TestRunSummaryReport(
            SchemaVersion,
            input.RunId,
            input.StartedAtUtc,
            input.CompletedAtUtc,
            Math.Round((input.CompletedAtUtc - input.StartedAtUtc).TotalSeconds, 3),
            invocation,
            reportPath,
            outcome,
            countsComplete,
            invocationComplete,
            artifactsComplete,
            fullMatrixInvocation,
            hasPerTargetProviderTotals,
            validForEvidence,
            input.OverallExitCode,
            input.Total,
            input.Passed,
            input.Failed,
            input.Skipped,
            timings,
            runtimeEnvironment,
            runnerEvidence,
            Array.AsReadOnly(expectedResults),
            Array.AsReadOnly(builds),
            Array.AsReadOnly(results),
            Array.AsReadOnly(artifactPaths),
            input.Failure is null
                ? null
                : input.Failure with { Message = SanitizeFailureMessage(input.Failure.Message) },
            input.TeardownFailure is null
                ? null
                : input.TeardownFailure with { Message = SanitizeFailureMessage(input.TeardownFailure.Message) });
    }

    public static int ResolveExitCode(TestRunSummaryReport report, int processExitCode)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (processExitCode != 0)
            return processExitCode;

        return report.Outcome == TestRunSummaryOutcome.Passed &&
               report.IsCompleteForInvocation &&
               report.ArtifactsComplete
            ? 0
            : 1;
    }

    public static string SanitizeFailureMessage(string? message, params string?[] secrets)
    {
        var sanitized = string.IsNullOrWhiteSpace(message)
            ? "Failure details unavailable."
            : message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        foreach (var secret in secrets
                     .Where(static secret => !string.IsNullOrWhiteSpace(secret))
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(static secret => secret!.Length))
        {
            var sqlEscapedSecret = secret!.Replace("'", "''", StringComparison.Ordinal);
            if (!string.Equals(sqlEscapedSecret, secret, StringComparison.Ordinal))
                sanitized = sanitized.Replace(sqlEscapedSecret, "[REDACTED]", StringComparison.Ordinal);
            sanitized = sanitized.Replace(secret!, "[REDACTED]", StringComparison.Ordinal);
        }

        sanitized = CredentialAssignmentPattern.Replace(sanitized, "${name}=[REDACTED]");
        sanitized = new string(sanitized
            .Where(static character => character is '\n' or '\t' || !char.IsControl(character))
            .ToArray());
        if (sanitized.Length <= MaximumFailureMessageLength)
            return sanitized;

        var retainedLength = MaximumFailureMessageLength - 1;
        if (retainedLength > 0 && char.IsHighSurrogate(sanitized[retainedLength - 1]))
            retainedLength--;
        return string.Concat(sanitized.AsSpan(0, retainedLength), "…");
    }

    public static void InvalidateExistingReport(string repositoryRoot, string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        var fullPath = Path.GetFullPath(reportPath);
        var artifactRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts"));
        if (!IsArtifactOutputPath(fullPath, artifactRoot))
        {
            throw new InvalidDataException(
                "The test summary path must remain under the repository artifact root without reparse-point parents.");
        }

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    public static void Write(TestRunSummaryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(report.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported test summary schema '{report.SchemaVersion}'.");

        var reportPath = Path.GetFullPath(report.ReportPath);
        var artifactRoot = Path.GetFullPath(Path.Combine(report.Invocation.RepositoryRoot, "artifacts"));
        if (!IsArtifactOutputPath(reportPath, artifactRoot))
            throw new InvalidDataException("The test summary path must remain under the repository artifact root without reparse-point parents.");
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        if (!IsArtifactOutputPath(reportPath, artifactRoot))
            throw new InvalidDataException("The test summary path became unsafe while preparing its output directory.");

        var temporaryPath = Path.Combine(
            directory ?? Environment.CurrentDirectory,
            $".{Path.GetFileName(reportPath)}.{Guid.NewGuid():N}.tmp");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(report, options),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static TestRunSummaryRepositoryState CaptureRepositoryState(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            return UnavailableRepositoryState;

        try
        {
            var root = Path.GetFullPath(repositoryRoot);
            if (!Directory.Exists(root))
                return UnavailableRepositoryState;

            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GCM_INTERACTIVE"] = "Never"
            };
            var commit = ExternalProcessRunner.Execute(
                "git",
                ["--no-optional-locks", "rev-parse", "HEAD"],
                root,
                environment);
            var branch = ExternalProcessRunner.Execute(
                "git",
                ["--no-optional-locks", "rev-parse", "--abbrev-ref", "HEAD"],
                root,
                environment);
            var status = ExternalProcessRunner.Execute(
                "git",
                ["--no-optional-locks", "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none"],
                root,
                environment);

            var commitValue = commit.StandardOutput.Trim();
            if (commit.ExitCode != 0 || branch.ExitCode != 0 || status.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(commitValue))
            {
                return UnavailableRepositoryState;
            }

            var branchValue = branch.StandardOutput.Trim();
            if (branchValue.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(branchValue))
                branchValue = "(detached)";

            var normalizedStatus = status.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
            return new TestRunSummaryRepositoryState(
                Captured: true,
                Commit: commitValue,
                Branch: branchValue,
                Dirty: !string.IsNullOrWhiteSpace(normalizedStatus),
                StatusSha256: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedStatus))).ToLowerInvariant());
        }
        catch
        {
            return UnavailableRepositoryState;
        }
    }

    public static (TestRunSummaryRunnerAssembly EntryAssembly, TestRunSummaryRunnerAssembly DevToolsAssembly)
        CaptureRunnerAssemblies() =>
        (
            CaptureRunnerAssembly(Assembly.GetEntryAssembly()),
            CaptureRunnerAssembly(typeof(TestRunSummaryReporter).Assembly)
        );

    private static TestRunSummaryInvocation Normalize(TestRunSummaryInvocation invocation)
    {
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(invocation.RepositoryRoot));
        var selectedTargets = invocation.SelectedTargets.ToArray();
        var resolvedSuites = invocation.ResolvedSuites
            .Select(static suite => suite with { ProjectPath = Path.GetFullPath(suite.ProjectPath) })
            .ToArray();
        var safeEnvironment = Normalize(invocation.SafeEnvironment);
        return invocation with
        {
            RepositoryRoot = repositoryRoot,
            SelectedTargets = Array.AsReadOnly(selectedTargets),
            ResolvedSuites = Array.AsReadOnly(resolvedSuites),
            SafeEnvironment = safeEnvironment,
            Plan = string.IsNullOrWhiteSpace(invocation.Plan) ? null : invocation.Plan.Trim(),
            IncludesAllSuites = HasExactSet(
                resolvedSuites.Select(static suite => suite.Name),
                ReleaseSuites.Keys),
            IncludesAllTargets = HasExactSet(
                selectedTargets.Select(static target => target.Id),
                ReleaseTargets.Keys),
            IsUnfiltered = string.IsNullOrWhiteSpace(invocation.Filter)
        };
    }

    private static TestRunSummarySafeEnvironment Normalize(TestRunSummarySafeEnvironment environment)
    {
        var hostIsSafe = TryNormalizeDatabaseHost(environment.DatabaseHostOverride, out var host);
        var valid = environment.DatabaseHostOverrideValid &&
                    ((!environment.DatabaseHostOverridePresent && environment.DatabaseHostOverride is null) ||
                     (environment.DatabaseHostOverridePresent && hostIsSafe));
        return environment with
        {
            DatabaseHostOverrideValid = valid,
            DatabaseHostOverride = valid && environment.DatabaseHostOverridePresent ? host : null
        };
    }

    private static TestRunSummaryExpectedResult Normalize(TestRunSummaryExpectedResult expected) =>
        expected with
        {
            ProjectPath = Path.GetFullPath(expected.ProjectPath),
            TargetIds = Array.AsReadOnly(expected.TargetIds.ToArray())
        };

    private static TestRunSummaryBuild Normalize(TestRunSummaryBuild build) =>
        build with
        {
            ProjectPath = Path.GetFullPath(build.ProjectPath),
            Arguments = Array.AsReadOnly(build.Arguments.ToArray()),
            WorkingDirectory = Path.GetFullPath(build.WorkingDirectory),
            LogPath = Path.GetFullPath(build.LogPath)
        };

    private static TestRunSummaryResult Normalize(TestRunSummaryResult result)
    {
        var targetIds = Array.AsReadOnly(result.TargetIds.ToArray());
        var environmentTargetIds = Array.AsReadOnly(result.Environment.TargetIds.ToArray());
        string? databaseHost = null;
        var hostCaptured = result.Environment.DatabaseHostCaptured &&
                           TryNormalizeDatabaseHost(result.Environment.DatabaseHost, out databaseHost);
        var artifacts = result.ArtifactPaths
            .Append(result.LogPath)
            .Append(result.HtmlReportPath)
            .Append(result.TrxReportPath)
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();
        var performance = Normalize(result.Performance);
        var outcome = DetermineResultOutcome(result);
        return result with
        {
            ProjectPath = Path.GetFullPath(result.ProjectPath),
            TargetIds = targetIds,
            Targets = targetIds.Count == 0 ? "-" : string.Join(", ", targetIds),
            Outcome = outcome,
            Arguments = Array.AsReadOnly(result.Arguments.ToArray()),
            WorkingDirectory = Path.GetFullPath(result.WorkingDirectory),
            Environment = result.Environment with
            {
                DatabaseHostCaptured = hostCaptured,
                DatabaseHost = hostCaptured ? databaseHost : null,
                TargetIds = environmentTargetIds
            },
            ArtifactPaths = Array.AsReadOnly(artifacts),
            LogPath = Path.GetFullPath(result.LogPath),
            HtmlReportPath = Path.GetFullPath(result.HtmlReportPath),
            TrxReportPath = Path.GetFullPath(result.TrxReportPath),
            InfrastructureSetupDurationSeconds = RoundSeconds(result.InfrastructureSetupDurationSeconds),
            Performance = performance
        };
    }

    private static TestRunSummaryPerformance Normalize(TestRunSummaryPerformance performance)
    {
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(performance.SlowestTests);
        ArgumentNullException.ThrowIfNull(performance.SlowestClasses);
        return performance with
        {
            CaptureError = performance.CaptureError is null
                ? null
                : SanitizeFailureMessage(performance.CaptureError),
            SlowestTests = Array.AsReadOnly(performance.SlowestTests.Take(20).ToArray()),
            SlowestClasses = Array.AsReadOnly(performance.SlowestClasses.Take(20).ToArray())
        };
    }

    private static TestRunSummaryOutcome DetermineResultOutcome(TestRunSummaryResult result)
    {
        if (result.ExitCode != 0 || result.Failed is > 0)
            return TestRunSummaryOutcome.Failed;
        if (result.Total is null || result.Passed is null || result.Failed is null || result.Skipped is null)
            return TestRunSummaryOutcome.Incomplete;
        if (!CountsAreNonnegativeAndConsistent(result.Total.Value, result.Passed.Value, result.Failed.Value, result.Skipped.Value))
            return TestRunSummaryOutcome.Incomplete;
        if (!result.Performance.Captured || result.Performance.TestCount != result.Total.Value)
            return TestRunSummaryOutcome.Incomplete;
        return TestRunSummaryOutcome.Passed;
    }

    private static TestRunSummaryOutcome DetermineOutcome(
        TestRunSummaryReportInput input,
        IReadOnlyList<TestRunSummaryResult> results,
        bool invocationComplete,
        bool countsComplete,
        bool artifactsComplete)
    {
        if (input.Failure is not null || input.TeardownFailure is not null)
            return TestRunSummaryOutcome.Error;
        if (input.OverallExitCode != 0 || input.Failed is > 0 ||
            results.Any(static result => result.Outcome == TestRunSummaryOutcome.Failed))
        {
            return TestRunSummaryOutcome.Failed;
        }
        if (!countsComplete || !invocationComplete || !artifactsComplete || results.Count == 0 ||
            results.Any(static result => result.Outcome != TestRunSummaryOutcome.Passed))
        {
            return TestRunSummaryOutcome.Incomplete;
        }
        return TestRunSummaryOutcome.Passed;
    }

    private static bool HasCompleteCounts(
        TestRunSummaryReportInput input,
        IReadOnlyList<TestRunSummaryResult> results)
    {
        if (input.Total is null || input.Passed is null || input.Failed is null || input.Skipped is null ||
            results.Count == 0 ||
            !results.All(static result =>
            result.Total is not null &&
            result.Passed is not null &&
            result.Failed is not null &&
            result.Skipped is not null))
        {
            return false;
        }

        return CountsAreNonnegativeAndConsistent(
                   input.Total.Value,
                   input.Passed.Value,
                   input.Failed.Value,
                   input.Skipped.Value) &&
               results.All(static result => CountsAreNonnegativeAndConsistent(
                   result.Total!.Value,
                   result.Passed!.Value,
                   result.Failed!.Value,
                   result.Skipped!.Value)) &&
               input.Total == results.Sum(static result => result.Total!.Value) &&
               input.Passed == results.Sum(static result => result.Passed!.Value) &&
               input.Failed == results.Sum(static result => result.Failed!.Value) &&
               input.Skipped == results.Sum(static result => result.Skipped!.Value);
    }

    private static bool IsInvocationComplete(
        TestRunSummaryInvocation invocation,
        IReadOnlyList<TestRunSummaryExpectedResult> expected,
        IReadOnlyList<TestRunSummaryBuild> builds,
        IReadOnlyList<TestRunSummaryResult> results,
        bool countsComplete)
    {
        if (!countsComplete ||
            !invocation.SafeEnvironment.DatabaseHostOverrideValid ||
            !string.Equals(
                invocation.SafeEnvironment.ProviderSetForTargetBatches,
                "targets",
                StringComparison.OrdinalIgnoreCase) ||
            !invocation.SafeEnvironment.ClearsTargetAliasForTargetBatches)
            return false;

        if (invocation.SelectedTargets.Select(static target => target.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != invocation.SelectedTargets.Count ||
            invocation.ResolvedSuites.Select(static suite => suite.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != invocation.ResolvedSuites.Count)
        {
            return false;
        }

        var derivedExpected = DeriveExpectedResults(invocation);
        if (derivedExpected.Count == 0 || expected.Count != derivedExpected.Count || results.Count != expected.Count)
            return false;

        var derivedKeys = derivedExpected.Select(ResultKey).Order(StringComparer.Ordinal).ToArray();
        var expectedKeys = expected.Select(ResultKey).Order(StringComparer.Ordinal).ToArray();
        var actualKeys = results.Select(ResultKey).Order(StringComparer.Ordinal).ToArray();
        if (expectedKeys.Distinct(StringComparer.Ordinal).Count() != expectedKeys.Length ||
            actualKeys.Distinct(StringComparer.Ordinal).Count() != actualKeys.Length ||
            !derivedKeys.SequenceEqual(expectedKeys, StringComparer.Ordinal) ||
            !expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal))
            return false;

        if (results.Any(static result =>
                !result.Performance.Captured ||
                result.Total is null ||
                result.Performance.TestCount != result.Total.Value))
        {
            return false;
        }

        if (results.Any(result => !IsCommandEnvironmentComplete(invocation, result)))
            return false;

        var databaseHosts = results
            .Where(static result => result.Environment.UsesDatabaseHost)
            .Select(static result => result.Environment.DatabaseHost!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (databaseHosts.Length > 1)
            return false;

        if (!invocation.BuildProject)
            return builds.Count == 0;

        var expectedProjects = invocation.ResolvedSuites
            .Select(static suite => suite.ProjectPath)
            .Distinct(PathComparer)
            .Order(PathComparer)
            .ToArray();
        var builtProjects = builds
            .Select(static build => build.ProjectPath)
            .Order(PathComparer)
            .ToArray();
        return builds.Count == expectedProjects.Length &&
               builds.All(static build => build.ExitCode == 0) &&
               builtProjects.Distinct(PathComparer).Count() == builtProjects.Length &&
               expectedProjects.SequenceEqual(builtProjects, PathComparer);
    }

    private static string ResultKey(TestRunSummaryExpectedResult result) =>
        $"{result.Suite}\n{result.ProjectPath}\n{result.BatchIndex?.ToString() ?? "-"}\n{string.Join("\n", result.TargetIds)}";

    private static string ResultKey(TestRunSummaryResult result) =>
        $"{result.Suite}\n{result.ProjectPath}\n{result.BatchIndex?.ToString() ?? "-"}\n{string.Join("\n", result.TargetIds)}";

    private static IReadOnlyList<TestRunSummaryExpectedResult> DeriveExpectedResults(
        TestRunSummaryInvocation invocation)
    {
        var expected = new List<TestRunSummaryExpectedResult>();
        foreach (var suite in invocation.ResolvedSuites)
        {
            if (!suite.UsesTargetBatches)
            {
                expected.Add(new TestRunSummaryExpectedResult(
                    suite.Name,
                    suite.ProjectPath,
                    BatchIndex: null,
                    TargetIds: Array.Empty<string>()));
                continue;
            }

            var suiteTargets = suite.IncludeSqliteTargets
                ? invocation.SelectedTargets.ToArray()
                : invocation.SelectedTargets.Where(static target => target.UsesPodman).ToArray();
            for (var index = 0; index < suiteTargets.Length; index += invocation.BatchSize)
            {
                expected.Add(new TestRunSummaryExpectedResult(
                    suite.Name,
                    suite.ProjectPath,
                    BatchIndex: (index / invocation.BatchSize) + 1,
                    TargetIds: suiteTargets
                        .Skip(index)
                        .Take(invocation.BatchSize)
                        .Select(static target => target.Id)
                        .ToArray()));
            }
        }

        return expected;
    }

    private static bool IsCommandEnvironmentComplete(
        TestRunSummaryInvocation invocation,
        TestRunSummaryResult result)
    {
        var isTargetBatch = result.TargetIds.Count > 0;
        var selectedTargets = result.TargetIds
            .Select(id => invocation.SelectedTargets.FirstOrDefault(target =>
                string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (selectedTargets.Any(static target => target is null))
            return false;

        var usesDatabaseHost = selectedTargets.Any(static target => target!.UsesPodman);
        if (result.Environment.UsesDatabaseHost != usesDatabaseHost)
            return false;
        if (usesDatabaseHost)
        {
            if (!result.Environment.DatabaseHostCaptured || result.Environment.DatabaseHost is null)
                return false;
            if (invocation.SafeEnvironment.DatabaseHostOverridePresent &&
                !string.Equals(
                    result.Environment.DatabaseHost,
                    invocation.SafeEnvironment.DatabaseHostOverride,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else if (result.Environment.DatabaseHostCaptured || result.Environment.DatabaseHost is not null)
        {
            return false;
        }

        if (isTargetBatch)
        {
            return result.Environment.UsesExplicitTargetSet &&
                   result.Environment.TargetAliasCleared &&
                   result.TargetIds.SequenceEqual(
                       result.Environment.TargetIds,
                       StringComparer.OrdinalIgnoreCase);
        }

        return !result.Environment.UsesExplicitTargetSet &&
               result.Environment.TargetAliasCleared &&
               result.Environment.TargetIds.Count == 0;
    }

    private static bool HasPerTargetProviderCoverage(
        TestRunSummaryInvocation invocation,
        IReadOnlyList<TestRunSummaryExpectedResult> expected)
    {
        var derivedProviderRows = DeriveExpectedResults(invocation)
            .Where(static result => result.TargetIds.Count > 0)
            .ToArray();
        if (derivedProviderRows.Length == 0 ||
            derivedProviderRows.Any(static result => result.TargetIds.Count != 1))
        {
            return false;
        }

        var derivedKeys = derivedProviderRows.Select(ResultKey).Order(StringComparer.Ordinal).ToArray();
        var suppliedKeys = expected
            .Where(static result => result.TargetIds.Count > 0)
            .Select(ResultKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return derivedKeys.SequenceEqual(suppliedKeys, StringComparer.Ordinal);
    }

    private static bool HasCanonicalReleaseScope(TestRunSummaryInvocation invocation)
    {
        if (!string.Equals(invocation.Suite, "all", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(invocation.ProjectPath) ||
            !invocation.IncludesAllSuites ||
            !invocation.IncludesAllTargets ||
            !invocation.IsUnfiltered ||
            invocation.ResolvedSuites.Count != ReleaseSuites.Count ||
            invocation.SelectedTargets.Count != ReleaseTargets.Count)
        {
            return false;
        }

        foreach (var suite in invocation.ResolvedSuites)
        {
            if (!ReleaseSuites.TryGetValue(suite.Name, out var contract) ||
                suite.UsesTargetBatches != contract.UsesTargetBatches ||
                suite.IncludeSqliteTargets != contract.IncludeSqliteTargets ||
                !PathComparer.Equals(
                    suite.ProjectPath,
                    Path.GetFullPath(Path.Combine(invocation.RepositoryRoot, contract.RelativeProjectPath))))
            {
                return false;
            }
        }

        foreach (var target in invocation.SelectedTargets)
        {
            if (!ReleaseTargets.TryGetValue(target.Id, out var contract) ||
                target.UsesPodman != contract.UsesPodman ||
                target.HostPort != contract.HostPort)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExactSet(IEnumerable<string> actual, IEnumerable<string> expected)
    {
        var actualValues = actual.ToArray();
        var expectedValues = expected.ToArray();
        return actualValues.Length == expectedValues.Length &&
               actualValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() == actualValues.Length &&
               actualValues.ToHashSet(StringComparer.OrdinalIgnoreCase)
                   .SetEquals(expectedValues);
    }

    private static bool CountsAreNonnegativeAndConsistent(int total, int passed, int failed, int skipped) =>
        total > 0 &&
        passed >= 0 &&
        failed >= 0 &&
        skipped >= 0 &&
        (long)passed + failed + skipped == total;

    private static double RoundSeconds(double value) => Math.Round(value, 3);

    private static bool HasCompleteArtifacts(
        string repositoryRoot,
        string reportPath,
        IReadOnlyList<TestRunSummaryBuild> builds,
        IReadOnlyList<TestRunSummaryResult> results)
    {
        var artifactRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts"));
        return results.Count > 0 &&
               IsArtifactOutputPath(reportPath, artifactRoot) &&
               builds.All(build => IsArtifactFile(build.LogPath, artifactRoot)) &&
               results.SelectMany(static result => result.ArtifactPaths)
                   .All(path => IsArtifactFile(path, artifactRoot));
    }

    private static bool IsArtifactFile(string path, string artifactRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!HasSafeArtifactPath(fullPath, artifactRoot, allowMissingLeaf: false))
                return false;

            var attributes = File.GetAttributes(fullPath);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsArtifactOutputPath(string path, string artifactRoot)
    {
        try
        {
            return HasSafeArtifactPath(Path.GetFullPath(path), artifactRoot, allowMissingLeaf: true);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSafeArtifactPath(string fullPath, string artifactRoot, bool allowMissingLeaf)
    {
        var relativePath = Path.GetRelativePath(artifactRoot, fullPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Directory.Exists(artifactRoot))
            return allowMissingLeaf;

        var rootAttributes = File.GetAttributes(artifactRoot);
        if ((rootAttributes & FileAttributes.Directory) == 0 ||
            (rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = artifactRoot;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (!Directory.Exists(current))
                return allowMissingLeaf;

            var directoryAttributes = File.GetAttributes(current);
            if ((directoryAttributes & FileAttributes.Directory) == 0 ||
                (directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        if (!File.Exists(fullPath))
            return allowMissingLeaf;
        var leafAttributes = File.GetAttributes(fullPath);
        return (leafAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    private static bool TryNormalizeDatabaseHost(string? value, out string? host)
    {
        host = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (string.Equals(candidate, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            host = "localhost";
            return true;
        }
        if (!IPAddress.TryParse(candidate, out var address))
            return false;

        host = address.ToString();
        return true;
    }

    private static bool HasRepositoryStateChanged(
        TestRunSummaryRepositoryState start,
        TestRunSummaryRepositoryState end) =>
        !start.Captured ||
        !end.Captured ||
        !string.Equals(start.Commit, end.Commit, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(start.Branch, end.Branch, StringComparison.Ordinal) ||
        start.Dirty != end.Dirty ||
        !string.Equals(start.StatusSha256, end.StatusSha256, StringComparison.OrdinalIgnoreCase);

    private static bool AssembliesMatchCheckout(
        TestRunSummaryRepositoryState start,
        TestRunSummaryRepositoryState end,
        TestRunSummaryRunnerAssembly entry,
        TestRunSummaryRunnerAssembly devTools) =>
        start.Captured &&
        end.Captured &&
        entry.RepositoryCommitCaptured &&
        devTools.RepositoryCommitCaptured &&
        string.Equals(start.Commit, end.Commit, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.RepositoryCommit, start.Commit, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(devTools.RepositoryCommit, start.Commit, StringComparison.OrdinalIgnoreCase);

    private static bool IsCleanAssembly(TestRunSummaryRunnerAssembly assembly, string expectedName) =>
        string.Equals(assembly.Name, expectedName, StringComparison.Ordinal) &&
        string.Equals(assembly.RepositoryBuildState, CleanRepositoryBuildState, StringComparison.Ordinal);

    public static TestRunSummaryRunnerAssembly CaptureRunnerAssembly(Assembly? assembly)
    {
        if (assembly is null)
            return UnknownRunnerAssembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var repositoryCommit = CompatibilitySizeReporter
            .ExtractRepositoryCommitFromInformationalVersion(informationalVersion);
        var buildStates = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static attribute => attribute.Key.Equals(RepositoryBuildStateMetadataName, StringComparison.Ordinal))
            .Select(static attribute => attribute.Value)
            .ToArray();
        var buildState = buildStates.Length == 1 && !string.IsNullOrWhiteSpace(buildStates[0])
            ? buildStates[0]!
            : "unknown";
        return new TestRunSummaryRunnerAssembly(
            assembly.GetName().Name ?? "unknown",
            string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion,
            repositoryCommit ?? "unknown",
            repositoryCommit is not null,
            buildState);
    }

    private static void ValidateInvocation(TestRunSummaryInvocation invocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.Command);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.RepositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.Suite);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.Configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.OutputMode);
        ArgumentNullException.ThrowIfNull(invocation.SelectedTargets);
        ArgumentNullException.ThrowIfNull(invocation.ResolvedSuites);
        ArgumentNullException.ThrowIfNull(invocation.SafeEnvironment);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.SafeEnvironment.ProviderSetForTargetBatches);
        if (invocation.BatchSize is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(invocation), "The test summary batch size must be between 1 and 32.");
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ReleaseSuiteContract(
        string RelativeProjectPath,
        bool UsesTargetBatches,
        bool IncludeSqliteTargets);

    private sealed record ReleaseTargetContract(bool UsesPodman, int? HostPort);
}
