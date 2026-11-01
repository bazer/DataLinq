using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public sealed class SchemaValidationContractTests
{
    [Test]
    public async Task DefaultsRequireErrorsOrAnyTypedIssueWithoutFilteringFindings()
    {
        var options = CapturedSchemaValidationOptions.Capture(null);
        await Assert.That(options.FailOnSeverity).IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(options.TreatValidationIssuesAsFailures).IsTrue();
        await Assert.That(options.CommandTimeout).IsNull();
        await Assert.That(options.CommandTimeoutSeconds).IsNull();
        await Assert.That(options.Include).IsEmpty();
        await Assert.That(options.MetadataReaderLog).IsNull();

        var result = Result([Difference(SchemaDifferenceSeverity.Info), Difference(SchemaDifferenceSeverity.Warning)], [], options);
        await Assert.That(result.HasDifferences).IsTrue();
        await Assert.That(result.HasFailures).IsFalse();
        await Assert.That(result.Differences.Count).IsEqualTo(2);
        await Assert.That(result.DatabaseName).IsEqualTo("effective database");
        await Assert.That(result.DatabaseType).IsEqualTo(DatabaseType.SQLite);
        await Assert.That(result.ModelTableCount).IsEqualTo(2);
        await Assert.That(result.DatabaseTableCount).IsEqualTo(3);
    }

    [Test]
    [Arguments(SchemaDifferenceSeverity.Info, SchemaDifferenceSeverity.Info, true)]
    [Arguments(SchemaDifferenceSeverity.Info, SchemaDifferenceSeverity.Warning, true)]
    [Arguments(SchemaDifferenceSeverity.Info, SchemaDifferenceSeverity.Error, true)]
    [Arguments(SchemaDifferenceSeverity.Warning, SchemaDifferenceSeverity.Info, false)]
    [Arguments(SchemaDifferenceSeverity.Warning, SchemaDifferenceSeverity.Warning, true)]
    [Arguments(SchemaDifferenceSeverity.Warning, SchemaDifferenceSeverity.Error, true)]
    [Arguments(SchemaDifferenceSeverity.Error, SchemaDifferenceSeverity.Info, false)]
    [Arguments(SchemaDifferenceSeverity.Error, SchemaDifferenceSeverity.Warning, false)]
    [Arguments(SchemaDifferenceSeverity.Error, SchemaDifferenceSeverity.Error, true)]
    public async Task DifferenceThresholdTruthTable(SchemaDifferenceSeverity threshold, SchemaDifferenceSeverity severity, bool fails)
    {
        var result = Result([Difference(severity)], [], CapturedSchemaValidationOptions.Capture(new() { FailOnSeverity = threshold }));
        await Assert.That(result.HasFailures).IsEqualTo(fails);
        await Assert.That(result.HasDifferences).IsTrue();
        await Assert.That(result.Differences.Single().Severity).IsEqualTo(severity);
    }

    [Test]
    [Arguments(DataLinqDiagnosticSeverity.Warning, false)]
    [Arguments(DataLinqDiagnosticSeverity.Warning, true)]
    [Arguments(DataLinqDiagnosticSeverity.Error, false)]
    [Arguments(DataLinqDiagnosticSeverity.Error, true)]
    public async Task IssuesFailIndependentlyOfSeverityAndRemainWhenIgnored(DataLinqDiagnosticSeverity severity, bool issueFailures)
    {
        var options = CapturedSchemaValidationOptions.Capture(new() { TreatValidationIssuesAsFailures = issueFailures });
        var issue = new DataLinqDiagnosticIssue(severity, DLFailureType.NotImplemented, "observed limitation");
        var result = Result([], [issue], options);
        await Assert.That(result.HasDifferences).IsFalse();
        await Assert.That(result.HasFailures).IsEqualTo(issueFailures);
        await Assert.That(result.Issues.Single().Severity).IsEqualTo(severity);
        // Turning off issue failures must not turn off real drift failures.
        await Assert.That(Result([Difference(SchemaDifferenceSeverity.Error)], [issue], options).HasFailures).IsTrue();
        await Assert.That(Result([], [], options).HasFailures).IsFalse();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(3)]
    [Arguments(int.MaxValue)]
    public async Task UndefinedThresholdIsRejected(int value)
    {
        await Assert.That(() => CapturedSchemaValidationOptions.Capture(new() { FailOnSeverity = (SchemaDifferenceSeverity)value }))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(null, null)]
    [Arguments(0L, 0)]
    [Arguments(1L, 1)]
    [Arguments(10_000_000L, 1)]
    [Arguments(10_000_001L, 2)]
    [Arguments(21_474_830_000_000L, 2_147_483)]
    public async Task CommandTimeoutPreservesDefaultUnlimitedAndCeiling(long? ticks, int? seconds)
    {
        var timeout = ticks is { } value ? TimeSpan.FromTicks(value) : (TimeSpan?)null;
        var options = CapturedSchemaValidationOptions.Capture(new() { CommandTimeout = timeout });
        await Assert.That(options.CommandTimeout).IsEqualTo(timeout);
        await Assert.That(options.CommandTimeoutSeconds).IsEqualTo(seconds);
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(-10_000L)]
    [Arguments(21_474_830_000_001L)]
    [Arguments(long.MaxValue)]
    public async Task InvalidCommandTimeoutIsRejected(long ticks)
    {
        await Assert.That(() => CapturedSchemaValidationOptions.Capture(new() { CommandTimeout = TimeSpan.FromTicks(ticks) }))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" \t")]
    public async Task MalformedIncludeIsRejectedBeforeProviderMetadataIsNeeded(string? name)
    {
        await Assert.That(() => CapturedSchemaValidationOptions.Capture(new() { Include = [name!] })).Throws<ArgumentException>();
    }

    [Test]
    public async Task ConfigurationRunsOnceAndCapturesAllMutableInputs()
    {
        var calls = 0;
        var logCalls = 0;
        var names = new List<string> { "Orders" };
        DataLinqSchemaValidationOptions? retained = null;
        var captured = CapturedSchemaValidationOptions.Configure(options =>
        {
            calls++;
            retained = options;
            options.Include = names;
            options.CommandTimeout = TimeSpan.FromTicks(1);
            options.FailOnSeverity = SchemaDifferenceSeverity.Warning;
            options.TreatValidationIssuesAsFailures = false;
            options.MetadataReaderLog = _ => logCalls++;
        });
        names[0] = "Changed";
        retained!.Include = [];
        retained.CommandTimeout = TimeSpan.MaxValue;
        retained.FailOnSeverity = SchemaDifferenceSeverity.Error;
        retained.TreatValidationIssuesAsFailures = true;
        retained.MetadataReaderLog = _ => throw new Exception("Must not use replacement callback.");

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(captured.Include.Single()).IsEqualTo("Orders");
        await Assert.That(captured.CommandTimeout).IsEqualTo(TimeSpan.FromTicks(1));
        await Assert.That(captured.CommandTimeoutSeconds).IsEqualTo(1);
        await Assert.That(captured.FailOnSeverity).IsEqualTo(SchemaDifferenceSeverity.Warning);
        await Assert.That(captured.TreatValidationIssuesAsFailures).IsFalse();
        captured.MetadataReaderLog!("diagnostic");
        await Assert.That(logCalls).IsEqualTo(1);
        await Assert.That(() => ((IList<string>)captured.Include)[0] = "Changed").Throws<NotSupportedException>();
    }

    [Test]
    public async Task ConfigurationFailureEscapesSynchronouslyWithOriginalIdentity()
    {
        var original = new InvalidOperationException("configuration failure");
        Exception? observed = null;
        try { CapturedSchemaValidationOptions.Configure(_ => throw original); }
        catch (Exception failure) { observed = failure; }
        await Assert.That(observed).IsSameReferenceAs(original);
    }

    [Test]
    public async Task ResultsDefensivelyCopyCollectionsAndIssueContextWhileRetainingDifferences()
    {
        var original = Difference(SchemaDifferenceSeverity.Warning);
        var differences = new List<SchemaDifference> { original };
        var context = new List<string> { "original context" };
        var issue = new DataLinqDiagnosticIssue(DataLinqDiagnosticSeverity.Warning, DLFailureType.NotImplemented,
            "original issue", objectPath: "Orders.Index", contextMessages: context);
        var issues = new List<DataLinqDiagnosticIssue> { issue };
        var options = new DataLinqSchemaValidationOptions();
        var result = Result(differences, issues, CapturedSchemaValidationOptions.Capture(options));
        var differencesOnly = Result(differences, [], CapturedSchemaValidationOptions.Capture(options));

        differences.Clear();
        issues.Clear();
        context[0] = "changed context";
        options.FailOnSeverity = SchemaDifferenceSeverity.Info;
        options.TreatValidationIssuesAsFailures = false;

        await Assert.That(result.Differences.Single()).IsSameReferenceAs(original);
        await Assert.That(result.Issues.Single().ContextMessages.Single()).IsEqualTo("original context");
        await Assert.That(result.Issues.Single().Message).IsEqualTo("original issue");
        await Assert.That(result.Issues.Single().ObjectPath).IsEqualTo("Orders.Index");
        await Assert.That(result.Issues.Single().FailureType).IsEqualTo(DLFailureType.NotImplemented);
        await Assert.That(result.HasDifferences).IsTrue();
        await Assert.That(result.HasFailures).IsTrue();
        await Assert.That(differencesOnly.HasFailures).IsFalse();
        await Assert.That(() => ((IList<SchemaDifference>)result.Differences).Clear()).Throws<NotSupportedException>();
        await Assert.That(() => ((IList<DataLinqDiagnosticIssue>)result.Issues).Clear()).Throws<NotSupportedException>();
        await Assert.That(() => ((IList<string>)result.Issues[0].ContextMessages)[0] = "changed").Throws<NotSupportedException>();
    }

    [Test]
    public async Task ChangingIssuePolicyCannotChangeAnExistingIssueOnlyResult()
    {
        var options = new DataLinqSchemaValidationOptions();
        var issue = new DataLinqDiagnosticIssue(DataLinqDiagnosticSeverity.Warning, DLFailureType.NotImplemented, "limitation");
        var result = Result([], [issue], CapturedSchemaValidationOptions.Capture(options));
        options.TreatValidationIssuesAsFailures = false;
        await Assert.That(result.HasFailures).IsTrue();
        await Assert.That(result.HasDifferences).IsFalse();
        await Assert.That(Result([], [issue], CapturedSchemaValidationOptions.Capture(options)).HasFailures).IsFalse();
    }

    [Test]
    public async Task PublicResultIsReadOnlyAndExceptionPreservesExactResult()
    {
        var type = typeof(DataLinqSchemaValidationResult);
        await Assert.That(type.IsSealed).IsTrue();
        await Assert.That(type.GetConstructors()).IsEmpty();
        await Assert.That(type.GetProperties().All(property => property.SetMethod is null)).IsTrue();
        var result = Result([Difference(SchemaDifferenceSeverity.Error)], [], CapturedSchemaValidationOptions.Capture(null));
        var exception = new DataLinqSchemaValidationException(result);
        await Assert.That(exception.Result).IsSameReferenceAs(result);
        await Assert.That(exception.Message).Contains("effective database");
        await Assert.That(() => new DataLinqSchemaValidationException(null!)).Throws<ArgumentNullException>();
    }

    private static SchemaDifference Difference(SchemaDifferenceSeverity severity) =>
        new(SchemaDifferenceKind.ColumnDefaultMismatch, severity, SchemaDifferenceSafety.Ambiguous, "Orders.State",
            "Model default: 'pending'; database default: 'new'.");

    private static DataLinqSchemaValidationResult Result(IEnumerable<SchemaDifference> differences,
        IEnumerable<DataLinqDiagnosticIssue> issues, CapturedSchemaValidationOptions options) =>
        new("effective database", DatabaseType.SQLite, 2, 3, differences, issues, options);
}
