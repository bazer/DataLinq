using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RawDiagnostics_LegacyOwnedReaderSeparatesCommandCleanup(bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var diagnostics = ExecutionFailureScope.Begin();
        var reused = new Exception("command cleanup");
        var oldSecondary = new Exception("old cleanup");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 2, "raw"))
        {
            OnDispose = () => ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.Timeout,
                ExecutionFailureStage.CommandExecution, ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None,
                null, [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, oldSecondary)],
                operation: ExecutionOperationKind.Commit))
        };
        var commandDisposals = 0;
        var command = new ScriptedDbCommand(() =>
        {
            commandDisposals++;
            if (freshReport) ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.ProviderError,
                ExecutionFailureStage.CommandExecution, ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None,
                null, [], operation: ExecutionOperationKind.Save));
            throw reused;
        });
        var owner = new OwnedCommandDataReader(reader, command);
        var primary = new Exception("work");
        var failures = new ExecutionFailures();
        failures.AddReported(primary, ExecutionFailureStage.RowLoading);
        failures = owner.DisposeWithFailures(failures)!;
        _ = owner.DisposeWithFailures(failures);
        var context = failures.Snapshot(new(), ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null);
        await Assert.That(failures.Primary).IsSameReferenceAs(primary);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        var second = context.SecondaryFailures.Single();
        await Assert.That(second.Exception).IsSameReferenceAs(reused);
        await Assert.That(second.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(second.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Save : ExecutionOperationKind.Dispose);
        await Assert.That(second.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(commandDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("scalar", false, false, false)]
    [Arguments("scalar", false, false, true)]
    [Arguments("scalar", false, true, false)]
    [Arguments("scalar", false, true, true)]
    [Arguments("scalar", true, false, false)]
    [Arguments("scalar", true, false, true)]
    [Arguments("scalar", true, true, false)]
    [Arguments("scalar", true, true, true)]
    [Arguments("reader", false, false, false)]
    [Arguments("reader", false, false, true)]
    [Arguments("reader", false, true, false)]
    [Arguments("reader", false, true, true)]
    [Arguments("reader", true, false, false)]
    [Arguments("reader", true, false, true)]
    [Arguments("reader", true, true, false)]
    [Arguments("reader", true, true, true)]
    [Arguments("rows", false, false, false)]
    [Arguments("rows", false, false, true)]
    [Arguments("rows", false, true, false)]
    [Arguments("rows", false, true, true)]
    [Arguments("rows", true, false, false)]
    [Arguments("rows", true, false, true)]
    [Arguments("rows", true, true, false)]
    [Arguments("rows", true, true, true)]
    public async Task RawDiagnostics_CleanupAndAssessmentOwnTheirOccurrence(string kind, bool asynchronous, bool cleanup, bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("work");
        var reused = new Exception("reused");
        var oldSecondary = new Exception("old nested cleanup");
        ExecutionFailureContext? earlier = null;
        void RecordEarlier()
        {
            using var nested = ExecutionFailureScope.Begin();
            earlier = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
                [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, oldSecondary)],
                operation: ExecutionOperationKind.Commit);
            ExecutionFailureContexts.Attach(reused, earlier);
        }
        void ReportCurrent()
        {
            if (freshReport)
                ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                    ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [],
                    operation: ExecutionOperationKind.Save));
        }
        void FailCurrent() { ReportCurrent(); throw reused; }
        var work = new AsyncCheckpoint(paused: true) { ReportingFailure = _ => RecordEarlier() };
        work.Fail(primary);
        var nativeReader = new ControlledAsyncDataReader { Advance = work };
        if (cleanup && kind != "scalar")
        {
            nativeReader.Cleanup = new(paused: true) { ReportingFailure = _ => ReportCurrent() };
            nativeReader.Cleanup.Fail(reused);
        }
        var native = new ControlledAsyncDatabaseAccess(kind == "scalar" ? work : null)
        {
            Reader = nativeReader, FailureEvidence = TrustedScalarRead,
            EvidenceFailure = cleanup ? null : reused,
            AssessingFailure = cleanup ? null : ReportCurrent
        };
        var eager = new ControlledEagerCommandFactory
        {
            Access = native,
            ConfigureCommand = factory => { if (cleanup) factory.Resource.Disposing = FailCurrent; }
        };
        fixture.Scenario.AsyncCommands = eager;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateBorrowedAccess = _ => native };
        var sync = EnableSyncRaw(fixture);
        var syncReader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 2, "raw"))
        {
            OnRead = () => { RecordEarlier(); throw primary; },
            OnDispose = cleanup ? FailCurrent : null
        };
        sync.Reader = () => syncReader;
        if (kind == "scalar")
        {
            sync.Executing = RecordEarlier;
            sync.ExecutionFailure = primary;
            if (cleanup) sync.CommandDisposing = FailCurrent;
        }
        if (!cleanup) { sync.Assessing = ReportCurrent; sync.EvidenceFailure = reused; }
        using var command = new ControlledCommand();
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (kind != "reader")
                await RunStandaloneRaw(transaction.DatabaseAccess, kind, asynchronous, kind == "scalar" ? null : command);
            else if (asynchronous)
            {
                await using var reader = await transaction.DatabaseAccess.ExecuteReaderAsyncCore(command);
                _ = await reader.ReadNextRowAsync(default);
            }
            else
            {
                using var reader = transaction.DatabaseAccess.ExecuteReader(command);
                _ = reader.ReadNextRow();
            }
        });
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        var second = context.SecondaryFailures.Single();
        await Assert.That(second.Exception).IsSameReferenceAs(reused);
        await Assert.That(second.Cause).IsEqualTo(cleanup && freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(second.Stage).IsEqualTo(cleanup ? ExecutionFailureStage.Cleanup : ExecutionFailureStage.Recovery);
        await Assert.That(second.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Save :
            cleanup ? ExecutionOperationKind.Dispose : ExecutionOperationKind.RawCommand);
        await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanup);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(earlier!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(earlier.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(earlier.SecondaryFailures.Single().Exception).IsSameReferenceAs(oldSecondary);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(asynchronous ? native.Calls.Count(x => x == "assess-failure") : sync.Assessments).IsEqualTo(1);
        if (kind == "scalar")
            await Assert.That(asynchronous ? eager.Commands.Single().Resource.AsyncDisposals : sync.CommandDisposals).IsEqualTo(1);
        else
            await Assert.That(asynchronous ? nativeReader.AsyncDisposeCalls : syncReader.Disposals).IsEqualTo(1);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }
}
