using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private sealed partial class ScriptedDatabaseTransaction
    {
        internal T RunCommandTelemetry<T>(IDbCommand command, string kind, Func<T> execute) =>
            ExecuteCommandWithTelemetry(command, kind, true, Type, execute);
    }

    [Test, NotInParallel]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("non_query")]
    public async Task SyncCommandTelemetry_ExecutionFailureKeepsPrimaryThroughEveryReporter(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand { CommandText = "SELECT secret_value" };
        var expected = new Exception("provider");
        var counter = new Exception("counter");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        using var caller = new Activity("command-caller").Start();
        using var activities = new CommandActivityProbe(stopping: _ => throw stop);
        using var metrics = new CommandMeterProbe(name => { throw name == "datalinq.db.commands" ? counter : duration; });
        var failure = Capture<Exception>(() => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
            .RunCommandTelemetry<int>(command, kind, () => throw expected));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([counter, duration, stop])).IsTrue();
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(1);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test, NotInParallel]
    public async Task SyncCommandTelemetry_SuccessReportingFailureCountsOnce()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand { CommandText = "UPDATE rows SET name = @name" };
        var expected = new Exception("counter");
        using var activities = new CommandActivityProbe();
        using var metrics = new CommandMeterProbe(name => { if (name == "datalinq.db.commands") throw expected; });
        var failure = Capture<Exception>(() => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
            .RunCommandTelemetry(command, "non_query", () => 1));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(metrics.Reports.All(report => report.Outcome == "success")).IsTrue();
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(0);
        await Assert.That(activities.Stopped.Single().Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
    }

    [Test, NotInParallel]
    public async Task SyncCommandTelemetry_UnreturnedReaderIsDisposedWithoutDisposingBorrowedCommand()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var reader = new ControlledAsyncDataReader { AllowSynchronousCalls = true };
        var expected = new Exception("stop");
        using var activities = new CommandActivityProbe(stopping: _ => throw expected);
        var failure = Capture<Exception>(() => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
            .RunCommandTelemetry(command, "reader", () => reader));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(reader.IsDisposed).IsTrue();
        await Assert.That(reader.SyncCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test, NotInParallel]
    public async Task SyncCommandTelemetry_StartFailureIsClassifiedWithoutDispatchOrLeakedActivity()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var expected = new Exception("start");
        var dispatched = false;
        using var caller = new Activity("command-caller").Start();
        using var activities = new CommandActivityProbe(starting: _ => throw expected);
        var failure = Capture<Exception>(() => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
            .RunCommandTelemetry(command, "scalar", () => { dispatched = true; return 17; }));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(dispatched).IsFalse();
        await Assert.That(ExecutionFailureContexts.Get(failure)?.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments("reader", true)]
    [Arguments("scalar", true)]
    [Arguments("non_query", true)]
    [Arguments("reader", false)]
    [Arguments("scalar", false)]
    [Arguments("non_query", false)]
    public async Task AsyncCommandTelemetry_SuccessCountsAtAcquisitionWithoutOwningBorrowedResources(string kind, bool observed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand { CommandText = "SELECT private_value", CommandTimeout = 23 };
        using var caller = new Activity("command-caller").Start();
        using var activities = observed ? new CommandActivityProbe() : null;
        using var metrics = observed ? new CommandMeterProbe() : null;
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess, ScalarResult = 17, NonQueryResult = 7 };
        var result = await ExecuteTelemetryAccessAsync(access, kind, command);
        await Assert.That(result).IsEqualTo(kind == "reader" ? access.Reader : kind == "scalar" ? 17 : 7);
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(0);
        await Assert.That(access.Reader.AsyncReadCalls).IsEqualTo(0);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.CommandText).IsEqualTo("SELECT private_value");
        await Assert.That(command.CommandTimeout).IsEqualTo(23);
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        if (observed)
        {
            await Assert.That(metrics!.Reports.Count).IsEqualTo(2);
            var activity = activities!.Stopped.Single();
            await Assert.That(activity.Parent).IsSameReferenceAs(caller);
            await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Ok);
            await Assert.That(activity.GetTagItem("db.operation.name")).IsEqualTo("select");
            await Assert.That(activity.GetTagItem("datalinq.command.kind")).IsEqualTo(kind);
            await Assert.That(activity.TagObjects.Any(tag => Equals(tag.Value, command.CommandText))).IsFalse();
        }
        if (result is IAsyncDataReader reader) await reader.DisposeAsync();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("non_query")]
    public async Task AsyncCommandTelemetry_ExecutionFailureKeepsPrimaryThroughEveryReporter(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var expected = new Exception("provider");
        var counter = new Exception("counter");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        using var activities = new CommandActivityProbe(stopping: _ => throw stop);
        using var metrics = new CommandMeterProbe(name => { throw name == "datalinq.db.commands" ? counter : duration; });
        var access = new ControlledAsyncDatabaseAccess(JournalFault(expected)) { TelemetryAccess = transaction.DatabaseAccess };
        var failure = await AsyncEnumerationFailureOf(() => ExecuteTelemetryAccessAsync(access, kind, command));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([counter, duration, stop])).IsTrue();
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCommandTelemetry_UnreturnedReaderCleanupSettlesBeforeOwnedCommandAndAdmission(bool cleanupFails)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("counter");
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess, FailureEvidence = TrustedScalarRead };
        access.Reader.Cleanup = new(paused: true);
        access.Reader.Cleanup.ReportingFailure = failure => ExecutionFailureContexts.Attach(failure,
            new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, transaction.TransactionID, [], operation: ExecutionOperationKind.Dispose));
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Cleanup = JournalFault(commandCleanup);
        using var activities = new CommandActivityProbe();
        using var metrics = new CommandMeterProbe(name => { if (name == "datalinq.db.commands") throw expected; });
        await using var rows = new AsyncReaderEnumerable<int>(() => new OwnedCommandExecution(access, factory, transaction.TransactionID),
            reader => reader.GetInt32(0), transaction).GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(activities.Stopped.Count).IsEqualTo(1);
            await Assert.That(access.Reader.AsyncReadCalls).IsEqualTo(0);
            await Assert.That(access.Reader.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally { if (cleanupFails) access.Reader.Cleanup.Fail(readerCleanup); else access.Reader.Cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(cleanupFails ? [readerCleanup, commandCleanup] : [commandCleanup])).IsTrue();
        await Assert.That(context.HasCleanupFailure).IsTrue();
        if (cleanupFails) await Assert.That(context.SecondaryFailures[0].Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(0);
    }

    [Test, NotInParallel]
    [Arguments("sample")]
    [Arguments("start")]
    [Arguments("cancel")]
    public async Task AsyncCommandTelemetry_StartFailurePreservesUndispatchedMutation(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var expected = new Exception("command " + phase);
        using var activities = new CommandActivityProbe(samplingFailure: phase == "sample" ? expected : null,
            starting: _ => { if (phase == "start") throw expected; if (phase == "cancel") cancellation.Cancel(); });
        using var metrics = new CommandMeterProbe();
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess,
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) };
        var factory = EnableAsyncMutations(fixture, access);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable, cancellation.Token));
        if (phase == "cancel") await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(metrics.Reports).IsEmpty();
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(0);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test, NotInParallel]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("non_query")]
    public async Task AsyncCommandTelemetry_CanceledNativeDispatchKeepsCancellationPrimary(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        using var command = new ControlledCommand();
        var stop = new Exception("stop");
        using var activities = new CommandActivityProbe(stopping: _ => throw stop);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { TelemetryAccess = transaction.DatabaseAccess };
        var pending = ExecuteTelemetryAccessAsync(access, kind, command, cancellation.Token);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var failure = await AsyncEnumerationFailureOf(() => pending);
        access.Dispatch.Release();
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test, NotInParallel]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("non_query")]
    public async Task AsyncCommandTelemetry_PreCancellationAndValidationEmitNothing(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var activities = new CommandActivityProbe();
        using var metrics = new CommandMeterProbe();
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess };
        await Assert.That(await AsyncEnumerationFailureOf(() => ExecuteTelemetryAccessAsync(access, kind, command, new(true)))).IsTypeOf<OperationCanceledException>();
        var expected = access.ValidationFailure = new NotSupportedException("unsupported");
        await Assert.That(await AsyncEnumerationFailureOf(() => ExecuteTelemetryAccessAsync(access, kind, command, new(true)))).IsSameReferenceAs(expected);
        await Assert.That(metrics.Reports).IsEmpty();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(access.ObservedCommand).IsNull();
    }

    [Test, NotInParallel]
    public async Task AsyncCommandTelemetry_DispatchedSaveKeepsRequestedKindAndPoisoning()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("command counter");
        using var metrics = new CommandMeterProbe(name => { if (name == "datalinq.db.commands") throw expected; });
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess, NonQueryResult = 1,
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) };
        var factory = EnableAsyncMutations(fixture, access);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(0);
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test, NotInParallel]
    public async Task SyncCommandTelemetry_ReaderCleanupFailureIsSecondary()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var expected = new Exception("counter");
        var cleanup = new Exception("reader cleanup");
        var reader = new ControlledAsyncDataReader { AllowSynchronousCalls = true, SyncDisposalFailure = cleanup };
        using var metrics = new CommandMeterProbe(name => { if (name == "datalinq.db.commands") throw expected; });
        var failure = Capture<Exception>(() => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
            .RunCommandTelemetry(command, "reader", () => reader));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test, NotInParallel]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("non_query")]
    public async Task AsyncCommandTelemetry_CancellationAfterNativeSuccessDoesNotUndoResult(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        using var activities = new CommandActivityProbe(stopping: _ => cancellation.Cancel());
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess, ScalarResult = 17, NonQueryResult = 7 };
        var result = await ExecuteTelemetryAccessAsync(access, kind, command, cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(result).IsEqualTo(kind == "reader" ? access.Reader : kind == "scalar" ? 17 : 7);
        await Assert.That(CommandCounts(fixture).Failures).IsEqualTo(0);
        if (result is IAsyncDataReader reader) await reader.DisposeAsync();
    }

    [Test, NotInParallel]
    [Arguments("borrowed-reader")]
    [Arguments("owned-reader")]
    [Arguments("borrowed-eager")]
    public async Task AsyncCommandTelemetry_PreDispatchReportingPreservesRawReuse(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var expected = new Exception("start");
        using var activities = new CommandActivityProbe(starting: _ => throw expected);
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess, FailureEvidence = TrustedScalarRead };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Access = access };
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (kind == "borrowed-eager") await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(command);
            else if (kind == "owned-reader") await transaction.DatabaseAccess.ExecuteReaderAsyncCore("SELECT value");
            else await transaction.DatabaseAccess.ExecuteReaderAsyncCore(command);
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        _ = transaction.Query();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCommandTelemetry_DispatchEvidenceRejectsStaleOrDifferentCommands(bool nestedDifferentCommand)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("reused failure");
        var factory = new ControlledOwnedCommandFactory();
        using var otherCommand = new ControlledCommand();
        void FailBeforeDispatch(IDbCommand command)
        {
            using var listener = new CommandActivityProbe(starting: _ => throw expected);
            _ = Capture<Exception>(() => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
                .RunCommandTelemetry(command, "non_query", () => 1));
        }
        if (!nestedDifferentCommand) FailBeforeDispatch(factory.Resource.Borrowed);
        var dispatch = JournalFault(expected);
        if (nestedDifferentCommand) dispatch.ReportingFailure = _ => FailBeforeDispatch(otherCommand);
        var access = new ControlledAsyncDatabaseAccess(dispatch);
        var owned = new OwnedCommandExecution(access, factory, transaction.TransactionID);
        await Assert.That(await AsyncEnumerationFailureOf(() => owned.ExecuteNonQueryAsync(default))).IsSameReferenceAs(expected);
        await Assert.That(access.ObservedCommand).IsSameReferenceAs(factory.Resource.Borrowed);
        await Assert.That(owned.Dispatched).IsTrue();
    }

    [Test, NotInParallel]
    public async Task AsyncCommandTelemetry_ReusedReporterAndCleanupExceptionRetainsCleanupFact()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var expected = new Exception("reused");
        using var activities = new CommandActivityProbe(stopping: _ => throw expected);
        using var metrics = new CommandMeterProbe(_ => throw expected);
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess };
        access.Reader.Cleanup = JournalFault(expected);
        var failure = await AsyncEnumerationFailureOf(() => access.ExecuteReaderAsync(command, default));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test, NotInParallel]
    public async Task AsyncCommandTelemetry_HookPreCancellationSuppliesNoDispatchEvidence()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var activities = new CommandActivityProbe();
        using var diagnostics = ExecutionFailureScope.Begin();
        var failure = await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteCommandWithTelemetryAsync(
            command, "non_query", true, transaction.DatabaseAccess.Type, new(true), () => Task.FromResult(1)));
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(CommandDispatchEvidence.ProvesNoDispatch(failure, command)).IsTrue();
        await Assert.That(activities.Stopped).IsEmpty();
    }

    [Test, NotInParallel]
    public async Task AsyncCommandTelemetry_DispatchEvidenceRequiresAnInvocationScope()
    {
        using var command = new ControlledCommand();
        var failure = new Exception("unscoped diagnostic lookup");
        await Assert.That(ExecutionFailureScope.Current).IsNull();
        ExecutionFailureContexts.Attach(failure, new(ExecutionFailureCause.LocalFinalizationError,
            ExecutionFailureStage.Finalization, ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [])
            { CommandDispatch = new(command, dispatched: false) });
        await Assert.That(CommandDispatchEvidence.ProvesNoDispatch(failure, command)).IsFalse();
    }

    [Test, NotInParallel]
    public async Task AsyncCommandTelemetry_LaterStartFailureCannotCommitWrittenBatchPrefix()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("second command start");
        var starts = 0;
        using var activities = new CommandActivityProbe(starting: _ => { if (++starts == 2) throw expected; });
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess, NonQueryResult = 1,
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) };
        var factory = EnableAsyncMutations(fixture, access, [1, "stored"]);
        var first = new Mutable<TransactionMutationGuardRow>();
        first["Id"] = 1; first["Value"] = "first";
        var second = new Mutable<TransactionMutationGuardRow>();
        second["Id"] = 2; second["Value"] = "second";
        var failure = await AsyncEnumerationFailureOf(() => transaction.InsertAsyncCore([first, second]));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(first.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(second.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.NoneForNew);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(factory.Commands.All(command => command.Resource.AsyncDisposals == 1)).IsTrue();
        await Assert.That(CommandCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        _ = Capture<Exception>(transaction.Commit);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCommandTelemetry_NoDispatchDoesNotBypassAssessmentOrCleanupFailure(bool cleanupFails)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("command start");
        var secondary = new Exception(cleanupFails ? "command cleanup" : "assessment");
        using var activities = new CommandActivityProbe(starting: _ => throw expected);
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess,
            FailureEvidence = TrustedScalarRead, EvidenceFailure = cleanupFails ? null : secondary };
        var factory = EnableAsyncMutations(fixture, access);
        if (cleanupFails) factory.ConfigureCommand = command => command.Resource.Cleanup = JournalFault(secondary);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(secondary);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(access.Calls.Contains("assess-failure")).IsTrue();
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(transaction.IsPoisoned).IsFalse();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCommandTelemetry_AdapterValidationBeforeHookRetainsNoDispatch(bool cancel)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        using var activities = new CommandActivityProbe();
        var expected = new NotSupportedException("provider validation rejected before hook");
        var validations = 0;
        var access = new ControlledAsyncDatabaseAccess { TelemetryAccess = transaction.DatabaseAccess,
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true),
            ValidatingCommand = () =>
            {
                if (++validations != 2) return;
                if (cancel) cancellation.Cancel(); else throw expected;
            } };
        EnableAsyncMutations(fixture, access);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable, cancellation.Token));
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
    }

    private static async Task<object?> ExecuteTelemetryAccessAsync(ControlledAsyncDatabaseAccess access, string kind,
        IDbCommand command, CancellationToken token = default) => kind switch
        {
            "reader" => await access.ExecuteReaderAsync(command, token),
            "scalar" => await access.ExecuteScalarAsync(command, token),
            _ => await access.ExecuteNonQueryAsync(command, token)
        };

    private static CommandMetricsSnapshot CommandCounts(ScriptedFixture fixture) =>
        DataLinqMetrics.Snapshot().Providers.SingleOrDefault(provider => provider.ProviderInstanceId == fixture.Provider.TelemetryInstanceId).Commands;

    private sealed class CommandActivityProbe : IDisposable
    {
        internal List<Activity> Stopped { get; } = [];
        private readonly ActivityListener listener;
        internal CommandActivityProbe(Action<Activity>? starting = null, Action<Activity>? stopping = null, Exception? samplingFailure = null)
        {
            listener = new()
            {
                ShouldListenTo = source => source.Name == "DataLinq",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Name == "datalinq.db.command" && samplingFailure is not null
                    ? throw samplingFailure : ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => { if (activity.OperationName == "datalinq.db.command") starting?.Invoke(activity); },
                ActivityStopped = activity => { if (activity.OperationName == "datalinq.db.command") { Stopped.Add(activity); stopping?.Invoke(activity); } }
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() => listener.Dispose();
    }

    private sealed class CommandMeterProbe : IDisposable
    {
        internal List<(string Name, string? Outcome)> Reports { get; } = [];
        private readonly MeterListener listener = new();
        internal CommandMeterProbe(Action<string>? recorded = null)
        {
            listener.InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == "DataLinq" && instrument.Name is "datalinq.db.commands" or "datalinq.db.command.duration")
                    meter.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                Reports.Add((instrument.Name, tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string));
                recorded?.Invoke(instrument.Name);
            });
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                Reports.Add((instrument.Name, tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string));
                recorded?.Invoke(instrument.Name);
            });
            listener.Start();
        }
        public void Dispose() => listener.Dispose();
    }
}
