using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.DevTools;
using DataLinq.Testing;

namespace DataLinq.Tests.Unit;

public sealed class TestRunSummaryReporterTests
{
    private const string Commit = "1234567890abcdef1234567890abcdef12345678";
    private static readonly string ExistingLogPath = CreateExistingLog();

    [Test]
    public async Task Create_ProducesCompleteCleanEvidenceAndPreservesLegacyTotals()
    {
        var input = CreateFullMatrixInput();

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.SchemaVersion).IsEqualTo("v0.9.testing-run-summary.v1");
        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Passed);
        await Assert.That(report.CountsComplete).IsTrue();
        await Assert.That(report.IsCompleteForInvocation).IsTrue();
        await Assert.That(report.ArtifactsComplete).IsTrue();
        await Assert.That(report.IsFullMatrixInvocation).IsTrue();
        await Assert.That(report.HasPerTargetProviderTotals).IsTrue();
        await Assert.That(report.ValidForEvidence).IsTrue();
        await Assert.That(report.Total).IsEqualTo(39);
        await Assert.That(report.Passed).IsEqualTo(39);
        await Assert.That(report.Results.Count).IsEqualTo(13);
        var unitResult = report.Results.Single(static result => result.Suite == "unit");
        await Assert.That(unitResult.Targets).IsEqualTo("-");
        await Assert.That(unitResult.TargetIds).IsEmpty();
        await Assert.That(unitResult.Outcome).IsEqualTo(TestRunSummaryOutcome.Passed);
        await Assert.That(report.ArtifactPaths.Count).IsEqualTo(2);
        await Assert.That(report.RunnerEvidence.ValidForEvidence).IsTrue();
        await Assert.That(TestRunSummaryReporter.ResolveExitCode(report, processExitCode: 0)).IsEqualTo(0);
    }

    [Test]
    public async Task Create_MissingCountIsIncompleteAndNeverPublishesPartialAggregate()
    {
        var result = CreateResult() with { Skipped = null };
        var input = CreateInput(results: [result], skipped: null);

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(report.CountsComplete).IsFalse();
        await Assert.That(report.IsCompleteForInvocation).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
        await Assert.That(report.Results[0].Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
    }

    [Test]
    public async Task Create_MismatchedAggregateIsIncomplete()
    {
        var report = TestRunSummaryReporter.Create(CreateInput(total: 2));

        await Assert.That(report.CountsComplete).IsFalse();
        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Create_ZeroOrImpossibleCountsAreIncomplete()
    {
        var zeroResult = CreateResult() with { Total = 0, Passed = 0 };
        var zeroReport = TestRunSummaryReporter.Create(CreateInput(
            results: [zeroResult],
            total: 0,
            passed: 0));
        var impossibleResult = CreateResult() with { Total = 3, Passed = 3, Skipped = 1 };
        var impossibleReport = TestRunSummaryReporter.Create(CreateInput(
            results: [impossibleResult],
            total: 3,
            passed: 3,
            skipped: 1));
        var negativeResult = CreateResult() with { Total = 3, Passed = 4, Skipped = -1 };
        var negativeReport = TestRunSummaryReporter.Create(CreateInput(
            results: [negativeResult],
            total: 3,
            passed: 4,
            skipped: -1));

        await Assert.That(zeroReport.CountsComplete).IsFalse();
        await Assert.That(zeroReport.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(impossibleReport.CountsComplete).IsFalse();
        await Assert.That(impossibleReport.Results[0].Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(negativeReport.CountsComplete).IsFalse();
        await Assert.That(negativeReport.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
    }

    [Test]
    public async Task Create_ZeroResultsIsIncomplete()
    {
        var input = CreateInput(
            results: Array.Empty<TestRunSummaryResult>(),
            total: null,
            passed: null,
            failed: null,
            skipped: null);

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(report.IsCompleteForInvocation).IsFalse();
        await Assert.That(report.Results).IsEmpty();
    }

    [Test]
    public async Task Create_PreservesStructuredProviderTargetsAndExpectedBatch()
    {
        var root = RepositoryRootLocator.Find();
        var projectPath = Path.Combine(root, "src", "DataLinq.Tests.Compliance", "DataLinq.Tests.Compliance.csproj");
        var targetIds = new[] { "sqlite-file", "mysql-8.4" };
        var result = CreateResult(projectPath) with
        {
            Suite = "compliance",
            BatchIndex = 1,
            Targets = "sqlite-file, mysql-8.4",
            TargetIds = targetIds,
            Environment = new TestRunSummaryCommandEnvironment(
                UsesDatabaseHost: true,
                DatabaseHostCaptured: true,
                DatabaseHost: "127.0.0.1",
                UsesExplicitTargetSet: true,
                TargetAliasCleared: true,
                TargetIds: targetIds)
        };
        var baseInput = CreateInput();
        var input = baseInput with
        {
            Invocation = baseInput.Invocation with
            {
                Alias = null,
                SelectedTargets =
                [
                    new TestRunSummaryTarget("sqlite-file", "SQLite file", "sqlite", UsesPodman: false, HostPort: null),
                    new TestRunSummaryTarget("mysql-8.4", "MySQL 8.4", "mysql", UsesPodman: true, HostPort: 13307)
                ],
                ResolvedSuites =
                [
                    new TestRunSummarySuite("compliance", projectPath, UsesTargetBatches: true, IncludeSqliteTargets: true)
                ],
                BatchSize = 2,
                Suite = "compliance"
            },
            ExpectedResults =
            [
                new TestRunSummaryExpectedResult("compliance", projectPath, BatchIndex: 1, targetIds)
            ],
            Results = [result]
        };

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.IsCompleteForInvocation).IsTrue();
        await Assert.That(string.Join(",", report.Invocation.SelectedTargets.Select(static target => target.Id)))
            .IsEqualTo("sqlite-file,mysql-8.4");
        await Assert.That(string.Join(",", report.Results[0].TargetIds))
            .IsEqualTo("sqlite-file,mysql-8.4");
        await Assert.That(report.Results[0].Targets).IsEqualTo("sqlite-file, mysql-8.4");
        await Assert.That(report.HasPerTargetProviderTotals).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Create_FocusedInvocationCannotSelfAttestAsReleaseEvidence()
    {
        var input = CreateInput();

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Passed);
        await Assert.That(report.IsCompleteForInvocation).IsTrue();
        await Assert.That(report.Invocation.IncludesAllSuites).IsFalse();
        await Assert.That(report.Invocation.IncludesAllTargets).IsFalse();
        await Assert.That(report.IsFullMatrixInvocation).IsFalse();
        await Assert.That(report.HasPerTargetProviderTotals).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
        await Assert.That(TestRunSummaryReporter.ResolveExitCode(report, processExitCode: 0)).IsEqualTo(0);
    }

    [Test]
    public async Task Create_FullMatrixRequiresCanonicalProviderCoverageAndResolvedHost()
    {
        var input = CreateFullMatrixInput();
        var omittedExpected = input.ExpectedResults
            .Where(static result => !(result.Suite == "compliance" && result.TargetIds.Contains("mariadb-11.8")))
            .ToArray();
        var omittedResults = input.Results
            .Where(static result => !(result.Suite == "compliance" && result.TargetIds.Contains("mariadb-11.8")))
            .ToArray();
        var missingCoverage = TestRunSummaryReporter.Create(input with
        {
            Total = 36,
            Passed = 36,
            ExpectedResults = omittedExpected,
            Results = omittedResults
        });
        var missingHostResults = input.Results
            .Select((result, index) => index == 5
                ? result with
                {
                    Environment = result.Environment with
                    {
                        DatabaseHostCaptured = false,
                        DatabaseHost = null
                    }
                }
                : result)
            .ToArray();
        var missingHost = TestRunSummaryReporter.Create(input with { Results = missingHostResults });

        await Assert.That(missingCoverage.IsCompleteForInvocation).IsFalse();
        await Assert.That(missingCoverage.ValidForEvidence).IsFalse();
        await Assert.That(missingHost.IsCompleteForInvocation).IsFalse();
        await Assert.That(missingHost.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Create_DropsUnsafeHostTextAndFailsClosed()
    {
        const string sentinel = "secret-bearing-host-value";
        var input = CreateFullMatrixInput();
        input = input with
        {
            Invocation = input.Invocation with
            {
                SafeEnvironment = input.Invocation.SafeEnvironment with
                {
                    DatabaseHostOverridePresent = true,
                    DatabaseHostOverrideValid = true,
                    DatabaseHostOverride = sentinel
                }
            }
        };

        var report = TestRunSummaryReporter.Create(input);
        var json = JsonSerializer.Serialize(report);

        await Assert.That(report.Invocation.SafeEnvironment.DatabaseHostOverrideValid).IsFalse();
        await Assert.That(report.Invocation.SafeEnvironment.DatabaseHostOverride).IsNull();
        await Assert.That(report.IsCompleteForInvocation).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
        await Assert.That(json).DoesNotContain(sentinel);
    }

    [Test]
    public async Task Create_MissingArtifactFailsEvidenceAndCommandClosed()
    {
        var missingLog = Path.Combine(Path.GetTempPath(), $"missing-test-summary-{Guid.NewGuid():N}.log");
        var result = CreateResult() with
        {
            LogPath = missingLog,
            ArtifactPaths = [missingLog]
        };

        var report = TestRunSummaryReporter.Create(CreateInput(results: [result]));

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(report.ArtifactsComplete).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
        await Assert.That(TestRunSummaryReporter.ResolveExitCode(report, processExitCode: 0)).IsEqualTo(1);
    }

    [Test]
    public async Task Create_ArtifactOutsideRepositoryArtifactRootFailsClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"datalinq-outside-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var outsideLog = Path.Combine(directory, "test.log");
            File.WriteAllText(outsideLog, "outside artifact");
            var result = CreateResult() with
            {
                LogPath = outsideLog,
                ArtifactPaths = [outsideLog]
            };

            var report = TestRunSummaryReporter.Create(CreateInput(results: [result]));

            await Assert.That(report.ArtifactsComplete).IsFalse();
            await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
            await Assert.That(TestRunSummaryReporter.ResolveExitCode(report, 0)).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Create_ReportOutsideRepositoryArtifactRootFailsClosedAndCannotBeWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"datalinq-outside-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "summary.json");
            var report = TestRunSummaryReporter.Create(CreateInput(reportPath: reportPath));
            InvalidDataException? exception = null;

            try
            {
                TestRunSummaryReporter.Write(report);
            }
            catch (InvalidDataException caught)
            {
                exception = caught;
            }

            await Assert.That(report.ArtifactsComplete).IsFalse();
            await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
            await Assert.That(report.ValidForEvidence).IsFalse();
            await Assert.That(exception).IsNotNull();
            await Assert.That(File.Exists(reportPath)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Create_CanonicalizesLegacyTargetsAndChecksCommandEnvironment()
    {
        var targetIds = new[] { "mysql-8.4" };
        var result = CreateResult() with
        {
            BatchIndex = 1,
            Targets = "contradictory legacy value",
            TargetIds = targetIds,
            Environment = new TestRunSummaryCommandEnvironment(
                UsesDatabaseHost: true,
                DatabaseHostCaptured: true,
                DatabaseHost: "127.0.0.1",
                UsesExplicitTargetSet: true,
                TargetAliasCleared: true,
                TargetIds: targetIds)
        };
        var input = CreateInput(results: [result]);
        input = input with
        {
            Invocation = input.Invocation with
            {
                SelectedTargets =
                [
                    new TestRunSummaryTarget("mysql-8.4", "MySQL 8.4", "MySql", true, 13307)
                ],
                ResolvedSuites =
                [
                    new TestRunSummarySuite("unit", result.ProjectPath, true, true)
                ]
            },
            ExpectedResults =
            [
                new TestRunSummaryExpectedResult("unit", result.ProjectPath, BatchIndex: 1, targetIds)
            ]
        };

        var report = TestRunSummaryReporter.Create(input);
        var mismatchedEnvironment = result with
        {
            Environment = result.Environment with { TargetIds = ["mariadb-11.8"] }
        };
        var mismatchedReport = TestRunSummaryReporter.Create(input with { Results = [mismatchedEnvironment] });
        var extraEnvironmentTarget = result with
        {
            Environment = result.Environment with { TargetIds = ["mysql-8.4", "mariadb-11.8"] }
        };
        var extraEnvironmentTargetReport = TestRunSummaryReporter.Create(input with
        {
            Results = [extraEnvironmentTarget]
        });

        await Assert.That(report.Results[0].Targets).IsEqualTo("mysql-8.4");
        await Assert.That(report.IsCompleteForInvocation).IsTrue();
        await Assert.That(mismatchedReport.IsCompleteForInvocation).IsFalse();
        await Assert.That(mismatchedReport.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(extraEnvironmentTargetReport.Results[0].Environment.TargetIds.Count).IsEqualTo(2);
        await Assert.That(extraEnvironmentTargetReport.IsCompleteForInvocation).IsFalse();
        await Assert.That(extraEnvironmentTargetReport.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
    }

    [Test]
    public async Task Create_ExplicitFailedCountFailsEvenWhenExitCodeIsZero()
    {
        var result = CreateResult() with { Passed = 2, Failed = 1 };
        var input = CreateInput(results: [result], passed: 2, failed: 1);

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Failed);
        await Assert.That(report.Results[0].Outcome).IsEqualTo(TestRunSummaryOutcome.Failed);
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Create_FatalFailureProducesErrorOutcome()
    {
        var input = CreateInput(
            overallExitCode: 1,
            failure: new TestRunSummaryFailure(
                "run-suites",
                typeof(InvalidOperationException).FullName!,
                "provisioning failed"));

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Error);
        await Assert.That(report.Failure).IsNotNull();
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Create_ExtraFailedBuildCannotBeIgnored()
    {
        var input = CreateInput();
        var projectPath = input.Invocation.ResolvedSuites[0].ProjectPath;
        input = input with
        {
            Invocation = input.Invocation with { BuildProject = true },
            Builds =
            [
                CreateBuild(projectPath, exitCode: 0),
                CreateBuild(projectPath, exitCode: 1)
            ]
        };

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.IsCompleteForInvocation).IsFalse();
        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Incomplete);
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task SanitizeFailureMessage_RedactsKnownSecretsAndBoundsOutput()
    {
        const string adminSecret = "admin-sentinel-93f5";
        const string appSecret = "app'sentinel-68a2";
        var sqlEscapedSecret = appSecret.Replace("'", "''", StringComparison.Ordinal);
        var raw = $"podman exec -e \"IDENTIFIED BY '{sqlEscapedSecret}'\" -p{adminSecret} {new string('x', 3_000)}";

        var sanitized = TestRunSummaryReporter.SanitizeFailureMessage(raw, adminSecret, appSecret);
        var genericallySanitized = TestRunSummaryReporter.SanitizeFailureMessage(
            "MARIADB_ROOT_PASSWORD=generic-sentinel");
        var report = TestRunSummaryReporter.Create(CreateInput(
            overallExitCode: 1,
            failure: new TestRunSummaryFailure("provision", "Example", sanitized)));
        var json = JsonSerializer.Serialize(report);

        await Assert.That(sanitized).DoesNotContain(adminSecret);
        await Assert.That(sanitized).DoesNotContain(appSecret);
        await Assert.That(sanitized).DoesNotContain(sqlEscapedSecret);
        await Assert.That(sanitized.Length).IsEqualTo(2_000);
        await Assert.That(genericallySanitized).IsEqualTo("MARIADB_ROOT_PASSWORD=[REDACTED]");
        await Assert.That(json).DoesNotContain(adminSecret);
        await Assert.That(json).DoesNotContain(appSecret);
    }

    [Test]
    public async Task SanitizeFailureMessage_DoesNotSplitSurrogatePairAtBoundary()
    {
        var raw = string.Concat(new string('a', 1_998), "😀", "tail");

        var sanitized = TestRunSummaryReporter.SanitizeFailureMessage(raw);

        await Assert.That(sanitized.EndsWith('…')).IsTrue();
        await Assert.That(char.IsSurrogate(sanitized[^2])).IsFalse();
        await Assert.That(sanitized.Length <= 2_000).IsTrue();
    }

    [Test]
    public async Task Create_DirtyOrStaleRunnerCannotBeValidEvidence()
    {
        var dirty = RepositoryState(dirty: true, statusSha: "dirty");
        var staleAssembly = RunnerAssembly("DataLinq.Testing.CLI") with
        {
            RepositoryCommit = "abcdefabcdefabcdefabcdefabcdefabcdefabcd"
        };
        var input = CreateInput(
            repositoryStart: dirty,
            repositoryEnd: dirty,
            entryAssembly: staleAssembly);

        var report = TestRunSummaryReporter.Create(input);

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Passed);
        await Assert.That(report.ValidForEvidence).IsFalse();
        await Assert.That(report.RunnerEvidence.AssembliesMatchCheckout).IsFalse();
    }

    [Test]
    public async Task Create_DirtyRunnerAssemblyCannotBeValidEvidence()
    {
        var dirtyAssembly = RunnerAssembly("DataLinq.Testing.CLI") with
        {
            RepositoryBuildState = "dirty"
        };

        var report = TestRunSummaryReporter.Create(CreateInput(entryAssembly: dirtyAssembly));

        await Assert.That(report.Outcome).IsEqualTo(TestRunSummaryOutcome.Passed);
        await Assert.That(report.RunnerEvidence.AssembliesMatchCheckout).IsTrue();
        await Assert.That(report.RunnerEvidence.AssembliesBuiltFromCleanState).IsFalse();
        await Assert.That(report.ValidForEvidence).IsFalse();
    }

    [Test]
    public async Task Write_UsesAdditivePascalCaseContractAndAtomicReplacement()
    {
        var directory = Path.Combine(
            RepositoryRootLocator.Find(),
            "artifacts",
            "test-results",
            $"datalinq-test-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "summary.json");
            var report = TestRunSummaryReporter.Create(CreateInput(reportPath: reportPath));

            TestRunSummaryReporter.Write(report);

            var bytes = File.ReadAllBytes(reportPath);
            await Assert.That(bytes.Length > 3).IsTrue();
            await Assert.That(bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf })).IsFalse();
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            await Assert.That(root.GetProperty("SchemaVersion").GetString())
                .IsEqualTo("v0.9.testing-run-summary.v1");
            await Assert.That(root.GetProperty("Outcome").GetString()).IsEqualTo("Passed");
            await Assert.That(root.GetProperty("Total").GetInt32()).IsEqualTo(3);
            await Assert.That(root.GetProperty("Results")[0].GetProperty("Targets").GetString()).IsEqualTo("-");
            await Assert.That(root.GetProperty("Results")[0].GetProperty("TargetIds").GetArrayLength()).IsEqualTo(0);
            await Assert.That(Directory.GetFiles(directory, ".*.tmp")).IsEmpty();

            var replacementReport = TestRunSummaryReporter.Create(CreateInput(
                reportPath: reportPath,
                overallExitCode: 1,
                failure: new TestRunSummaryFailure("run-suites", "Example", "replacement")));
            TestRunSummaryReporter.Write(replacementReport);
            using var replacement = JsonDocument.Parse(File.ReadAllBytes(reportPath));
            await Assert.That(replacement.RootElement.GetProperty("Outcome").GetString()).IsEqualTo("Error");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CaptureRepositoryState_MissingRootFailsClosed()
    {
        var state = TestRunSummaryReporter.CaptureRepositoryState(
            Path.Combine(Path.GetTempPath(), $"missing-datalinq-repo-{Guid.NewGuid():N}"));

        await Assert.That(state.Captured).IsFalse();
        await Assert.That(state.Dirty).IsTrue();
        await Assert.That(state.Commit).IsEqualTo("unknown");
    }

    [Test]
    public async Task InvalidateExistingReport_RemovesStaleSuccessfulOutput()
    {
        var root = RepositoryRootLocator.Find();
        var directory = Path.Combine(
            root,
            "artifacts",
            "test-results",
            $"datalinq-stale-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "summary.json");
            File.WriteAllText(reportPath, "{\"Outcome\":\"Passed\"}");

            TestRunSummaryReporter.InvalidateExistingReport(root, reportPath);

            await Assert.That(File.Exists(reportPath)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task InvalidateExistingReport_RejectsOutsidePathWithoutDeletingIt()
    {
        var root = RepositoryRootLocator.Find();
        var directory = Path.Combine(Path.GetTempPath(), $"datalinq-unsafe-stale-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "summary.json");
            File.WriteAllText(reportPath, "{\"Outcome\":\"Passed\"}");
            InvalidDataException? exception = null;

            try
            {
                TestRunSummaryReporter.InvalidateExistingReport(root, reportPath);
            }
            catch (InvalidDataException caught)
            {
                exception = caught;
            }

            await Assert.That(exception).IsNotNull();
            await Assert.That(File.Exists(reportPath)).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TestRunSummaryReportInput CreateFullMatrixInput()
    {
        var root = RepositoryRootLocator.Find();
        var targets = TestTargetCatalog.AllTargetIds
            .Select(targetId =>
            {
                var server = TestTargetCatalog.TryGetServerTarget(targetId);
                return new TestRunSummaryTarget(
                    targetId,
                    server?.DisplayName ?? targetId,
                    server?.Family.ToString() ?? "SQLite",
                    server is not null,
                    server?.HostPort);
            })
            .OrderBy(static target => target.UsesPodman)
            .ThenBy(static target => target.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suites = new[]
        {
            new TestRunSummarySuite(
                "generators",
                Path.Combine(root, "src", "DataLinq.Generators.Tests", "DataLinq.Generators.Tests.csproj"),
                false,
                false),
            new TestRunSummarySuite(
                "unit",
                Path.Combine(root, "src", "DataLinq.Tests.Unit", "DataLinq.Tests.Unit.csproj"),
                false,
                false),
            new TestRunSummarySuite(
                "memory",
                Path.Combine(root, "src", "DataLinq.Tests.Memory", "DataLinq.Tests.Memory.csproj"),
                false,
                false),
            new TestRunSummarySuite(
                "compliance",
                Path.Combine(root, "src", "DataLinq.Tests.Compliance", "DataLinq.Tests.Compliance.csproj"),
                true,
                true),
            new TestRunSummarySuite(
                "mysql",
                Path.Combine(root, "src", "DataLinq.Tests.MySql", "DataLinq.Tests.MySql.csproj"),
                true,
                false)
        };
        var expected = new List<TestRunSummaryExpectedResult>();
        foreach (var suite in suites.Where(static suite => !suite.UsesTargetBatches))
            expected.Add(new TestRunSummaryExpectedResult(suite.Name, suite.ProjectPath, null, Array.Empty<string>()));

        var complianceSuite = suites.Single(static suite => suite.Name == "compliance");
        for (var index = 0; index < targets.Length; index++)
        {
            expected.Add(new TestRunSummaryExpectedResult(
                complianceSuite.Name,
                complianceSuite.ProjectPath,
                index + 1,
                [targets[index].Id]));
        }

        var mysqlSuite = suites.Single(static suite => suite.Name == "mysql");
        var serverTargets = targets.Where(static target => target.UsesPodman).ToArray();
        for (var index = 0; index < serverTargets.Length; index++)
        {
            expected.Add(new TestRunSummaryExpectedResult(
                mysqlSuite.Name,
                mysqlSuite.ProjectPath,
                index + 1,
                [serverTargets[index].Id]));
        }

        var results = expected.Select(item =>
        {
            var selected = targets.Where(target => item.TargetIds.Contains(
                target.Id,
                StringComparer.OrdinalIgnoreCase)).ToArray();
            var usesDatabaseHost = selected.Any(static target => target.UsesPodman);
            return CreateResult(item.ProjectPath) with
            {
                Suite = item.Suite,
                BatchIndex = item.BatchIndex,
                TargetIds = item.TargetIds,
                Environment = new TestRunSummaryCommandEnvironment(
                    UsesDatabaseHost: usesDatabaseHost,
                    DatabaseHostCaptured: usesDatabaseHost,
                    DatabaseHost: usesDatabaseHost ? "127.0.0.1" : null,
                    UsesExplicitTargetSet: item.TargetIds.Count > 0,
                    TargetAliasCleared: true,
                    TargetIds: item.TargetIds)
            };
        }).ToArray();

        return new TestRunSummaryReportInput(
            StartedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 0, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 20, TimeSpan.Zero),
            Invocation: new TestRunSummaryInvocation(
                Command: "run",
                RepositoryRoot: root,
                Alias: "all",
                SelectedTargets: targets,
                ResolvedSuites: suites,
                SafeEnvironment: new TestRunSummarySafeEnvironment(true, true, "127.0.0.1", "targets", true),
                IncludesAllSuites: true,
                IncludesAllTargets: true,
                IsUnfiltered: true,
                Suite: "all",
                ProjectPath: null,
                Filter: null,
                Configuration: "Release",
                BuildProject: false,
                BatchSize: 1,
                ParallelSuites: false,
                TearDown: false,
                OutputMode: "Failures",
                Profile: ToolingProfile.Sandbox),
            ReportPath: Path.Combine(root, "artifacts", "test-results", "full-matrix-summary.json"),
            RepositoryStart: RepositoryState(),
            RepositoryEnd: RepositoryState(),
            EntryAssembly: RunnerAssembly("DataLinq.Testing.CLI"),
            DevToolsAssembly: RunnerAssembly("DataLinq.DevTools"),
            OverallExitCode: 0,
            Total: 39,
            Passed: 39,
            Failed: 0,
            Skipped: 0,
            ExpectedResults: expected,
            Builds: Array.Empty<TestRunSummaryBuild>(),
            Results: results,
            Failure: null);
    }

    private static TestRunSummaryReportInput CreateInput(
        string? reportPath = null,
        IReadOnlyList<TestRunSummaryResult>? results = null,
        int overallExitCode = 0,
        int? total = 3,
        int? passed = 3,
        int? failed = 0,
        int? skipped = 0,
        TestRunSummaryRepositoryState? repositoryStart = null,
        TestRunSummaryRepositoryState? repositoryEnd = null,
        TestRunSummaryRunnerAssembly? entryAssembly = null,
        TestRunSummaryFailure? failure = null)
    {
        var root = RepositoryRootLocator.Find();
        var projectPath = Path.Combine(root, "src", "DataLinq.Tests.Unit", "DataLinq.Tests.Unit.csproj");
        var actualResults = results ?? [CreateResult(projectPath)];
        return new TestRunSummaryReportInput(
            StartedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 0, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 2, TimeSpan.Zero),
            Invocation: new TestRunSummaryInvocation(
                Command: "run",
                RepositoryRoot: root,
                Alias: "quick",
                SelectedTargets:
                [
                    new TestRunSummaryTarget("sqlite-file", "SQLite file", "sqlite", UsesPodman: false, HostPort: null)
                ],
                ResolvedSuites:
                [
                    new TestRunSummarySuite("unit", projectPath, UsesTargetBatches: false, IncludeSqliteTargets: false)
                ],
                SafeEnvironment: new TestRunSummarySafeEnvironment(
                    DatabaseHostOverridePresent: true,
                    DatabaseHostOverrideValid: true,
                    DatabaseHostOverride: "127.0.0.1",
                    ProviderSetForTargetBatches: "targets",
                    ClearsTargetAliasForTargetBatches: true),
                IncludesAllSuites: true,
                IncludesAllTargets: true,
                IsUnfiltered: true,
                Suite: "unit",
                ProjectPath: null,
                Filter: null,
                Configuration: "Release",
                BuildProject: false,
                BatchSize: 1,
                ParallelSuites: false,
                TearDown: false,
                OutputMode: "Failures",
                Profile: ToolingProfile.Sandbox),
            ReportPath: reportPath ?? Path.Combine(root, "artifacts", "test-results", "unit-summary.json"),
            RepositoryStart: repositoryStart ?? RepositoryState(),
            RepositoryEnd: repositoryEnd ?? RepositoryState(),
            EntryAssembly: entryAssembly ?? RunnerAssembly("DataLinq.Testing.CLI"),
            DevToolsAssembly: RunnerAssembly("DataLinq.DevTools"),
            OverallExitCode: overallExitCode,
            Total: total,
            Passed: passed,
            Failed: failed,
            Skipped: skipped,
            ExpectedResults:
            [
                new TestRunSummaryExpectedResult("unit", projectPath, BatchIndex: null, TargetIds: Array.Empty<string>())
            ],
            Builds: Array.Empty<TestRunSummaryBuild>(),
            Results: actualResults,
            Failure: failure);
    }

    private static TestRunSummaryResult CreateResult(string? projectPath = null)
    {
        var root = RepositoryRootLocator.Find();
        var logPath = ExistingLogPath;
        return new TestRunSummaryResult(
            Suite: "unit",
            ProjectPath: projectPath ?? Path.Combine(root, "src", "DataLinq.Tests.Unit", "DataLinq.Tests.Unit.csproj"),
            BatchIndex: null,
            Targets: "-",
            TargetIds: Array.Empty<string>(),
            Outcome: TestRunSummaryOutcome.Incomplete,
            Executable: "dotnet",
            Arguments: ["run", "--project", "src/DataLinq.Tests.Unit"],
            WorkingDirectory: root,
            Environment: new TestRunSummaryCommandEnvironment(
                UsesDatabaseHost: false,
                DatabaseHostCaptured: false,
                DatabaseHost: null,
                UsesExplicitTargetSet: false,
                TargetAliasCleared: true,
                TargetIds: Array.Empty<string>()),
            StartedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 0, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 1, TimeSpan.Zero),
            DurationSeconds: 1,
            ExitCode: 0,
            Total: 3,
            Passed: 3,
            Failed: 0,
            Skipped: 0,
            ArtifactPaths: [logPath],
            LogPath: logPath);
    }

    private static string CreateExistingLog()
    {
        var root = RepositoryRootLocator.Find();
        var logPath = Path.Combine(root, "artifacts", "test-results", "test-run-summary-fixture.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, "test summary reporter fixture");
        return logPath;
    }

    private static TestRunSummaryBuild CreateBuild(string projectPath, int exitCode)
    {
        var root = RepositoryRootLocator.Find();
        var logPath = Path.Combine(
            root,
            "artifacts",
            "test-results",
            $"build-{exitCode}-{Guid.NewGuid():N}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, "test summary build fixture");
        return new TestRunSummaryBuild(
            ProjectPath: projectPath,
            Executable: "dotnet",
            Arguments: ["build", projectPath],
            WorkingDirectory: root,
            StartedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 0, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 8, 7, 8, 0, 1, TimeSpan.Zero),
            DurationSeconds: 1,
            ExitCode: exitCode,
            LogPath: logPath);
    }

    private static TestRunSummaryRepositoryState RepositoryState(
        bool dirty = false,
        string statusSha = "clean") =>
        new(
            Captured: true,
            Commit,
            Branch: "v0.9",
            Dirty: dirty,
            StatusSha256: statusSha);

    private static TestRunSummaryRunnerAssembly RunnerAssembly(string name) =>
        new(
            name,
            InformationalVersion: $"0.9.0+{Commit}",
            RepositoryCommit: Commit,
            RepositoryCommitCaptured: true,
            RepositoryBuildState: "clean");
}
