using System;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("reader", false)]
    [Arguments("reader", true)]
    public async Task RawDiagnostics_NoDispatchProofMustBeCurrentAndCommandSpecific(string kind, bool stale)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        using var command = new ControlledCommand();
        using var other = new ControlledCommand();
        var expected = new Exception("native");
        ExecutionFailureContext? earlier = null;
        void Report()
        {
            var context = new ExecutionFailureContext(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Continue, transaction.TransactionID, [],
                operation: ExecutionOperationKind.RawCommand, providerInstanceId: fixture.Provider.TelemetryInstanceId);
            CommandDispatchEvidence.Attach(expected, context, stale ? command : other, dispatched: false);
            earlier = ExecutionFailureContexts.Get(expected);
        }
        if (stale) Report();
        else factory.Executing = Report;
        factory.ExecutionFailure = expected;
        var failure = await AsyncEnumerationFailureOf(() => RunStandaloneRaw(transaction.DatabaseAccess, kind, false, command));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
        await Assert.That(factory.Executions.Count).IsEqualTo(1);
        await Assert.That(factory.Assessments).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(earlier!.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(earlier.Recovery).IsEqualTo(ExecutionRecoveryActions.Continue);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test]
    [Arguments("sync-read", false)]
    [Arguments("sync-read", true)]
    [Arguments("sync-dispose", false)]
    [Arguments("sync-dispose", true)]
    [Arguments("sync-rows", false)]
    [Arguments("sync-rows", true)]
    [Arguments("async-read", false)]
    [Arguments("async-read", true)]
    [Arguments("async-dispose", false)]
    [Arguments("async-dispose", true)]
    [Arguments("mixed-dispose", false)]
    [Arguments("mixed-dispose", true)]
    [Arguments("async-rows", false)]
    [Arguments("async-rows", true)]
    public async Task RawDiagnostics_ReaderRetainsIdentityAfterAcquisition(string kind, bool withProvider)
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("late reader");
        ExecutionFailureContext? nested = null;
        void Report()
        {
            nested = new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [],
                operation: ExecutionOperationKind.Commit, providerInstanceId: "foreign-provider",
                activeOperation: ExecutionOperationKind.Save);
            ExecutionFailureContexts.Attach(expected, nested);
        }
        void Fail() { Report(); throw expected; }
        var cleanup = kind.EndsWith("dispose", StringComparison.Ordinal);
        var checkpoint = new AsyncCheckpoint(paused: true) { ReportingFailure = _ => Report() };
        checkpoint.Fail(expected);
        var nativeReader = new ControlledAsyncDataReader { AllowSynchronousCalls = true };
        if (kind == "mixed-dispose") nativeReader.SyncDisposing = Fail;
        else if (cleanup) nativeReader.Cleanup = checkpoint;
        else nativeReader.Advance = checkpoint;
        var native = new ControlledAsyncDatabaseAccess { Reader = nativeReader };
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateBorrowedAccess = _ => native };
        var syncReader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 2, "raw"))
        {
            OnRead = cleanup ? null : Fail, OnDispose = cleanup ? Fail : null
        };
        var sync = new SyncRawTestFactory { Reader = () => syncReader };
        var asynchronous = !kind.StartsWith("sync", StringComparison.Ordinal);
        DatabaseAccess access = asynchronous
            ? new ScriptedDatabaseAccess(withProvider ? fixture.Provider : null, fixture.Scenario)
            : new SyncRawStandaloneTestAccess(sync, withProvider ? fixture.Provider : null);
        using var command = new ControlledCommand();
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (kind.EndsWith("rows", StringComparison.Ordinal))
                await RunStandaloneRaw(access, "rows", asynchronous, command);
            else if (asynchronous)
            {
                var reader = await access.ExecuteReaderAsyncCore(command);
                try
                {
                    if (kind == "mixed-dispose") reader.Dispose();
                    else if (cleanup) await reader.DisposeAsync();
                    else _ = await reader.ReadNextRowAsync(default);
                }
                // A failed call settles ownership; a later Dispose must not retry it.
                finally { await reader.DisposeAsync(); }
            }
            else
            {
                using var reader = access.ExecuteReader(command);
                if (cleanup) reader.Dispose();
                else _ = reader.ReadNextRow();
            }
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.ProviderInstanceId).IsEqualTo(withProvider ? fixture.Provider.TelemetryInstanceId : null);
        await Assert.That(context.Operation).IsEqualTo(cleanup ? ExecutionOperationKind.Dispose : ExecutionOperationKind.RawCommand);
        await Assert.That(context.ActiveOperation).IsNull();
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanup);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(nested!.ProviderInstanceId).IsEqualTo("foreign-provider");
        await Assert.That(nested.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(asynchronous ? nativeReader.AsyncDisposeCalls + nativeReader.SyncCalls : syncReader.Disposals).IsEqualTo(1);
    }
}
