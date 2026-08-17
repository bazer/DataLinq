using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public static class TestRunTrxReader
{
    private const int SlowResultLimit = 20;

    public static TestRunSummaryPerformance Read(
        string trxPath,
        double testHostDurationSeconds,
        int? configuredMaximumParallelTests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trxPath);

        var parallelismSource = configuredMaximumParallelTests.HasValue
            ? "TUNIT_MAX_PARALLEL_TESTS"
            : "auto";

        try
        {
            if (!File.Exists(trxPath))
            {
                return Unavailable(
                    "The TRX report was not produced.",
                    configuredMaximumParallelTests,
                    parallelismSource);
            }

            var document = XDocument.Load(trxPath, LoadOptions.None);
            var definitions = ReadDefinitions(document);
            var results = document
                .Descendants()
                .Where(static element => element.Name.LocalName == "UnitTestResult")
                .Select(element => ReadResult(element, definitions))
                .ToArray();

            if (results.Length == 0)
            {
                return Unavailable(
                    "The TRX report contains no test results.",
                    configuredMaximumParallelTests,
                    parallelismSource);
            }

            var durations = results
                .Select(static result => result.DurationSeconds)
                .OrderBy(static duration => duration)
                .ToArray();
            var totalDuration = durations.Sum();
            var slowestTests = results
                .OrderByDescending(static result => result.DurationSeconds)
                .ThenBy(static result => result.ClassName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static result => result.Name, StringComparer.Ordinal)
                .Take(SlowResultLimit)
                .Select(static result => new TestRunSummarySlowTest(
                    result.Name,
                    result.ClassName,
                    result.Outcome,
                    Round(result.DurationSeconds)))
                .ToArray();
            var slowestClasses = results
                .GroupBy(static result => result.ClassName ?? "(unknown)", StringComparer.Ordinal)
                .Select(static group => new TestRunSummarySlowClass(
                    group.Key,
                    group.Count(),
                    Round(group.Sum(static result => result.DurationSeconds)),
                    Round(group.Average(static result => result.DurationSeconds)),
                    Round(group.Max(static result => result.DurationSeconds))))
                .OrderByDescending(static result => result.TotalDurationSeconds)
                .ThenBy(static result => result.ClassName, StringComparer.Ordinal)
                .Take(SlowResultLimit)
                .ToArray();
            double? effectiveConcurrency = testHostDurationSeconds > 0
                ? totalDuration / testHostDurationSeconds
                : null;

            return new TestRunSummaryPerformance(
                Captured: true,
                CaptureError: null,
                TestCount: results.Length,
                TotalTestDurationSeconds: Round(totalDuration),
                P50DurationSeconds: Percentile(durations, 0.50),
                P95DurationSeconds: Percentile(durations, 0.95),
                P99DurationSeconds: Percentile(durations, 0.99),
                MaximumDurationSeconds: Round(durations[^1]),
                EffectiveConcurrency: effectiveConcurrency.HasValue ? Round(effectiveConcurrency.Value) : null,
                ConfiguredMaximumParallelTests: configuredMaximumParallelTests,
                ConfiguredParallelismSource: parallelismSource,
                SlowestTests: Array.AsReadOnly(slowestTests),
                SlowestClasses: Array.AsReadOnly(slowestClasses));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
        {
            return Unavailable(
                $"The TRX report could not be parsed: {exception.Message}",
                configuredMaximumParallelTests,
                parallelismSource);
        }
    }

    public static TestRunSummaryPerformance Unavailable(
        string error,
        int? configuredMaximumParallelTests = null,
        string? configuredParallelismSource = null) =>
        new(
            Captured: false,
            CaptureError: TestRunSummaryReporter.SanitizeFailureMessage(error),
            TestCount: 0,
            TotalTestDurationSeconds: 0,
            P50DurationSeconds: null,
            P95DurationSeconds: null,
            P99DurationSeconds: null,
            MaximumDurationSeconds: null,
            EffectiveConcurrency: null,
            ConfiguredMaximumParallelTests: configuredMaximumParallelTests,
            ConfiguredParallelismSource: configuredParallelismSource ??
                                         (configuredMaximumParallelTests.HasValue
                                             ? "TUNIT_MAX_PARALLEL_TESTS"
                                             : "auto"),
            SlowestTests: Array.Empty<TestRunSummarySlowTest>(),
            SlowestClasses: Array.Empty<TestRunSummarySlowClass>());

    private static IReadOnlyDictionary<string, TestDefinition> ReadDefinitions(XDocument document) =>
        document
            .Descendants()
            .Where(static element => element.Name.LocalName == "UnitTest")
            .Select(static element =>
            {
                var method = element.Elements().FirstOrDefault(static child => child.Name.LocalName == "TestMethod");
                return new
                {
                    Id = (string?)element.Attribute("id"),
                    Definition = new TestDefinition(
                        (string?)method?.Attribute("className"),
                        (string?)method?.Attribute("name"))
                };
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(static item => item.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Definition, StringComparer.OrdinalIgnoreCase);

    private static TestResult ReadResult(
        XElement element,
        IReadOnlyDictionary<string, TestDefinition> definitions)
    {
        var testId = (string?)element.Attribute("testId");
        definitions.TryGetValue(testId ?? string.Empty, out var definition);
        var name = (string?)element.Attribute("testName") ?? definition?.Name ?? "(unknown)";
        var outcome = (string?)element.Attribute("outcome") ?? "Unknown";
        var durationText = (string?)element.Attribute("duration");
        if (!TimeSpan.TryParse(durationText, CultureInfo.InvariantCulture, out var duration) || duration < TimeSpan.Zero)
            throw new InvalidDataException($"TRX test '{name}' has an invalid duration.");

        return new TestResult(name, definition?.ClassName, outcome, duration.TotalSeconds);
    }

    private static double Percentile(IReadOnlyList<double> orderedDurations, double percentile)
    {
        var nearestRank = Math.Max(1, (int)Math.Ceiling(percentile * orderedDurations.Count));
        return Round(orderedDurations[nearestRank - 1]);
    }

    private static double Round(double value) => Math.Round(value, 6);

    private sealed record TestDefinition(string? ClassName, string? Name);

    private sealed record TestResult(
        string Name,
        string? ClassName,
        string Outcome,
        double DurationSeconds);
}
