using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.SQLite;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("probe", false, false)]
    [Arguments("probe", false, true)]
    [Arguments("probe", true, false)]
    [Arguments("probe", true, true)]
    [Arguments("metadata", false, false)]
    [Arguments("metadata", false, true)]
    [Arguments("metadata", true, false)]
    [Arguments("metadata", true, true)]
    [Arguments("import", false, false)]
    [Arguments("import", false, true)]
    [Arguments("import", true, false)]
    [Arguments("import", true, true)]
    [Arguments("journal", false, false)]
    [Arguments("journal", false, true)]
    [Arguments("journal", true, false)]
    [Arguments("journal", true, true)]
    public async Task AdministrativeOccurrences_SessionCleanupCannotBorrowAnEarlierFailure(string kind, bool workFails, bool freshReport)
    {
        var occurrence = new AdministrativeFailureOccurrence();
        var probe = new ExistenceProbeHarness();
        var metadata = new MetadataHarness();
        var journal = new JournalModeHarness();
        var first = metadata.Queries["tables"];
        var command = kind switch { "probe" => probe.Commands.Resource, "journal" => journal.Commands.Resource, _ => first.Command.Resource };
        command.Disposing = occurrence.RecordEarlierFailure;
        var cleanup = occurrence.Cleanup(freshReport, deferred: workFails);
        probe.Session.Cleanup = cleanup;
        metadata.Session.Cleanup = cleanup;
        journal.Session.Cleanup = cleanup;
        if (workFails)
        {
            var access = new ControlledAsyncDatabaseAccess(ProbeFault(occurrence.WorkFailure));
            probe.Session.Access = access;
            journal.Session.Access = access;
            first.Access = access;
        }
        using var probeProvider = new ExistenceProbeProvider(new(), () => probe);
        using var metadataProvider = new MetadataTestProvider(new(), () => metadata);
        using var journalProvider = new JournalModeProvider(new(), () => journal);
        async Task<Exception> RunAsync() => kind switch
        {
            "probe" => await AsyncEnumerationFailureOf(() => probeProvider.DatabaseExistsAsyncCore()),
            "journal" => await AsyncEnumerationFailureOf(() => journalProvider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL)),
            "import" => MetadataException(await ImportMetadata(new MetadataTestFactory(() => metadata))),
            _ => MetadataException(await metadataProvider.ReadValidationMetadataAsyncCore())
        };
        var pending = RunAsync();
        if (workFails)
        {
            try
            {
                await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(command.AsyncDisposals).IsEqualTo(1);
            }
            finally { cleanup.Fail(occurrence.Reused); }
        }
        var failure = await pending;
        var owner = kind switch { "probe" => ExecutionOperationKind.ExistenceCheck, "journal" => ExecutionOperationKind.ProviderConfiguration, _ => ExecutionOperationKind.MetadataRead };
        var providerId = kind switch { "probe" => probeProvider.TelemetryInstanceId, "journal" => journalProvider.TelemetryInstanceId, "import" => null, _ => metadataProvider.TelemetryInstanceId };
        await occurrence.AssertResult(failure, workFails, freshReport, owner, providerId);
        await Assert.That(command.AsyncDisposals).IsEqualTo(1);
        await Assert.That(kind switch { "probe" => probe.Session.Disposals, "journal" => journal.Session.Disposals, _ => metadata.Session.Disposals }).IsEqualTo(1);
        await Assert.That(cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministrativeOccurrences_AvailabilityClassifierUsesOnlyItsOwnReports(bool freshReport)
    {
        var occurrence = new AdministrativeFailureOccurrence();
        var primary = new ProbeConnectionFailure();
        var harness = new ExistenceProbeHarness();
        harness.Session.Access = new ControlledAsyncDatabaseAccess(ProbeFault(primary));
        harness.Commands.Resource.Disposing = occurrence.RecordEarlierFailure;
        harness.Classify = _ =>
        {
            if (freshReport) occurrence.ReportFreshFailure(occurrence.Reused);
            throw occurrence.Reused;
        };
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore());
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(freshReport ? 2 : 1);
        var classification = context.SecondaryFailures[0];
        await Assert.That(classification.Exception).IsSameReferenceAs(occurrence.Reused);
        await Assert.That(classification.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(classification.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Validation);
        await Assert.That(classification.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.RawCommand : ExecutionOperationKind.ExistenceCheck);
        await Assert.That(context.HasCleanupFailure).IsEqualTo(freshReport);
        await Assert.That(harness.Classifications).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await occurrence.AssertEarlierSnapshot();
    }

    [Test]
    [Arguments("probe", false)]
    [Arguments("probe", true)]
    [Arguments("metadata", false)]
    [Arguments("metadata", true)]
    [Arguments("import", false)]
    [Arguments("import", true)]
    [Arguments("journal", false)]
    [Arguments("journal", true)]
    public async Task AdministrativeOccurrences_MissingSessionIsKnownButProviderConstructionFailureIsNot(string kind, bool missingSession)
    {
        var expected = new InvalidOperationException("provider construction failed");
        var probe = new ExistenceProbeHarness { Creating = () => missingSession ? null! : throw expected };
        var metadata = new MetadataHarness { Creating = () => missingSession ? null! : throw expected };
        var journal = new JournalModeHarness { Creating = () => missingSession ? null! : throw expected };
        using var probeProvider = new ExistenceProbeProvider(new(), () => probe);
        using var metadataProvider = new MetadataTestProvider(new(), () => metadata);
        using var journalProvider = new JournalModeProvider(new(), () => journal);
        var failure = kind switch
        {
            "probe" => await AsyncEnumerationFailureOf(() => probeProvider.DatabaseExistsAsyncCore()),
            "journal" => await AsyncEnumerationFailureOf(() => journalProvider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL)),
            "import" => MetadataException(await ImportMetadata(new MetadataTestFactory(() => metadata))),
            _ => MetadataException(await metadataProvider.ReadValidationMetadataAsyncCore())
        };
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        if (!missingSession) await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(missingSession ? ExecutionFailureCause.InvalidOperation : ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(context.Operation).IsEqualTo(kind switch { "probe" => ExecutionOperationKind.ExistenceCheck, "journal" => ExecutionOperationKind.ProviderConfiguration, _ => ExecutionOperationKind.MetadataRead });
        await Assert.That(context.ProviderInstanceId).IsEqualTo(kind switch { "probe" => probeProvider.TelemetryInstanceId, "journal" => journalProvider.TelemetryInstanceId, "import" => null, _ => metadataProvider.TelemetryInstanceId });
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(probe.Session.Disposals + metadata.Session.Disposals + journal.Session.Disposals).IsEqualTo(0);
        await Assert.That(probe.Session.Opens + journal.Session.Opens).IsEqualTo(0);
    }
}

internal sealed class AdministrativeFailureOccurrence
{
    internal Exception WorkFailure { get; } = new("native execution");
    internal Exception Reused { get; } = new("reused at session cleanup");
    private Exception EarlierSecondary { get; } = new("earlier nested cleanup");
    private Exception FreshSecondary { get; } = new("current nested cleanup");
    private ExecutionFailureContext? earlier;

    internal void RecordEarlierFailure()
    {
        using var nested = ExecutionFailureScope.Begin();
        try { throw Reused; }
        catch (Exception failure)
        {
            earlier = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
                [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, EarlierSecondary, ExecutionOperationKind.Dispose)],
                operation: ExecutionOperationKind.RawCommand);
            ExecutionFailureContexts.Attach(failure, earlier);
        }
    }

    internal AsyncCheckpoint Cleanup(bool freshReport, bool deferred = false)
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        if (!deferred) checkpoint.Fail(Reused);
        if (freshReport) checkpoint.ReportingFailure = ReportFreshFailure;
        return checkpoint;
    }

    internal void ReportFreshFailure(Exception failure) => ExecutionFailureContexts.Attach(failure, new(
        ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
        ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
        [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, FreshSecondary, ExecutionOperationKind.Dispose)],
        operation: ExecutionOperationKind.RawCommand));

    internal async Task AssertResult(Exception failure, bool workFails, bool freshReport, ExecutionOperationKind owner, string? providerId)
    {
        await Assert.That(failure).IsSameReferenceAs(workFails ? WorkFailure : Reused);
        var context = ExecutionFailureContexts.Get(failure)!;
        var cleanupCause = freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown;
        var cleanupOperation = freshReport ? ExecutionOperationKind.RawCommand : ExecutionOperationKind.Dispose;
        await Assert.That(context.Cause).IsEqualTo(workFails ? ExecutionFailureCause.Unknown : cleanupCause);
        await Assert.That(context.Stage).IsEqualTo(workFails ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Cleanup);
        await Assert.That(context.Operation).IsEqualTo(workFails ? owner : cleanupOperation);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo((workFails ? 1 : 0) + (freshReport ? 1 : 0));
        if (workFails)
        {
            var cleanup = context.SecondaryFailures[0];
            await Assert.That(cleanup.Exception).IsSameReferenceAs(Reused);
            await Assert.That(cleanup.Cause).IsEqualTo(cleanupCause);
            await Assert.That(cleanup.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
            await Assert.That(cleanup.Operation).IsEqualTo(cleanupOperation);
        }
        if (freshReport) await Assert.That(context.SecondaryFailures[^1].Exception).IsSameReferenceAs(FreshSecondary);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.ProviderInstanceId).IsEqualTo(providerId);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await AssertEarlierSnapshot();
    }

    internal async Task AssertEarlierSnapshot()
    {
        await Assert.That(earlier!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(earlier.SecondaryFailures[0].Exception).IsSameReferenceAs(EarlierSecondary);
    }
}
