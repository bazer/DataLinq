using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("buffered", false, false)]
    [Arguments("buffered", false, true)]
    [Arguments("buffered", true, false)]
    [Arguments("buffered", true, true)]
    [Arguments("stream", false, false)]
    [Arguments("stream", false, true)]
    [Arguments("stream", true, false)]
    [Arguments("stream", true, true)]
    [Arguments("buffer", false, false)]
    [Arguments("buffer", false, true)]
    [Arguments("buffer", true, false)]
    [Arguments("buffer", true, true)]
    [Arguments("continuation", false, false)]
    [Arguments("continuation", false, true)]
    [Arguments("continuation", true, false)]
    [Arguments("continuation", true, true)]
    public async Task MaterializationOccurrences_EachRowOwnsItsFailure(string kind, bool earlierRow, bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var reader = new ControlledAsyncDataReader([1, 2]);
        var native = new ControlledAsyncDatabaseAccess { Reader = reader, FailureEvidence = TrustedScalarRead };
        var commands = new ControlledOwnedCommandFactory();
        var source = new OwnedCommandExecution(native, commands);
        if (!earlierRow) reader.Reading = occurrence.RecordEarlier;
        var rows = 0;
        int Materialize(IAsyncDataReader current)
        {
            rows++;
            if (earlierRow && rows == 1) { occurrence.RecordEarlier(); return current.GetInt32(0); }
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        }
        var identity = new ReadExecutionIdentity(ExecutionOperationKind.Query, fixture.Provider.TelemetryInstanceId);
        var buffer = new OccurrenceBuffer(Materialize);
        var invocation = new AsyncReaderInvocation<int>(source, Materialize, transaction, Identity: identity);
        var sequence = kind switch
        {
            "buffer" => new AsyncReaderEnumerable<int>(() => invocation with { Materialize = null, Buffer = buffer }),
            "continuation" => new AsyncReaderEnumerable<int>(() => AsyncReaderTransform.Capture(invocation, static (values, _) => values)),
            _ => new AsyncReaderEnumerable<int>(() => invocation)
        };
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (kind == "buffered")
                _ = await new AsyncBufferedRead<int>(transaction, source, row => { _ = Materialize(row); },
                    () => rows, operationKind: ExecutionOperationKind.Query).ExecuteAsync(default);
            else
                _ = await PlanRows(sequence);
        });
        await AssertMaterializationOccurrence(failure, transaction.AsyncFailureContext!, occurrence, freshReport);
        await Assert.That(rows).IsEqualTo(earlierRow ? 2 : 1);
        await Assert.That(reader.AsyncReadCalls).IsEqualTo(rows);
        await Assert.That(reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(1);
        await Assert.That(buffer.Completed).IsFalse();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MaterializationOccurrences_CompletionOwnsItsFailure(bool continuation, bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var native = new ControlledAsyncDatabaseAccess { Reader = new([1]), FailureEvidence = TrustedScalarRead };
        var commands = new ControlledOwnedCommandFactory();
        if (continuation) commands.Resource.Disposing = occurrence.RecordEarlier;
        var source = new OwnedCommandExecution(native, commands);
        var identity = new ReadExecutionIdentity(ExecutionOperationKind.Query, fixture.Provider.TelemetryInstanceId);
        IReadOnlyList<int> Complete()
        {
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        }
        var buffer = new OccurrenceBuffer(row => { occurrence.RecordEarlier(); return row.GetInt32(0); }, Complete);
        var invocation = continuation
            ? AsyncReaderTransform.Capture(new AsyncReaderInvocation<int>(source, row => row.GetInt32(0), transaction, Identity: identity),
                (IReadOnlyList<int> _, CancellationToken _) => Complete())
            : new AsyncReaderInvocation<int>(source, null, transaction, Buffer: buffer, Identity: identity);
        var failure = await AsyncEnumerationFailureOf(() => PlanRows(new AsyncReaderEnumerable<int>(() => invocation)));
        await AssertMaterializationOccurrence(failure, transaction.AsyncFailureContext!, occurrence, freshReport,
            canContinue: !freshReport || !continuation);
        await Assert.That(native.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(freshReport && continuation ? 0 : 1);
        if (!freshReport || !continuation) _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MaterializationOccurrences_SingleRelationCompletionRejectsCleanupReport(bool freshReport)
    {
        using var fixture = new AsyncRelationFixture(reference: true);
        using var transaction = fixture.Provider.StartTransaction();
        var occurrence = new SettledFailureOccurrence();
        var native = fixture.SetRows();
        fixture.Factory.ConfigureCommand = command => command.Resource.Disposing = occurrence.RecordEarlier;
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationChild)).Model.RelationProperties[nameof(AsyncRelationChild.Parent)];
        using var ownership = DataSourceAccess.BeginRead(transaction, "complete relation", operationKind: ExecutionOperationKind.RelationLoad);
        var prepared = fixture.Cache.PrepareRelationRowsAsyncCore(1, property, transaction, ownership!.Step);
        var failure = await AsyncEnumerationFailureOf(() => fixture.Cache.ExecuteRelationRowsAsyncCore<int>(prepared, _ =>
        {
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        }, default));
        var context = ExecutionFailureContexts.Get(failure)!;
        await AssertMaterializationOccurrence(failure, context, occurrence, freshReport, ExecutionOperationKind.RelationLoad, !freshReport);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(freshReport ? 0 : 1);
        await Assert.That(fixture.Factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
    }


    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MaterializationOccurrences_NestedTransformOwnsItsFailure(bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var native = new ControlledAsyncDatabaseAccess { Reader = new([1]), FailureEvidence = TrustedScalarRead };
        var commands = new ControlledOwnedCommandFactory();
        var input = new AsyncReaderInvocation<int>(new OwnedCommandExecution(native, commands), row => row.GetInt32(0),
            transaction, Identity: new(ExecutionOperationKind.Query, fixture.Provider.TelemetryInstanceId));
        var first = AsyncReaderTransform.Capture(input, (IReadOnlyList<int> rows, CancellationToken _) =>
        {
            occurrence.RecordEarlier();
            return rows;
        });
        var second = AsyncReaderTransform.Capture<int, int>(first, (IReadOnlyList<int> _, CancellationToken _) =>
        {
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        });
        var failure = await AsyncEnumerationFailureOf(() => PlanRows(new AsyncReaderEnumerable<int>(() => second)));
        await AssertMaterializationOccurrence(failure, transaction.AsyncFailureContext!, occurrence, freshReport, canContinue: !freshReport);
        await Assert.That(native.Reader.AsyncReadCalls).IsEqualTo(2);
        await Assert.That(native.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(freshReport ? 0 : 1);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MaterializationOccurrences_ContinuationRecoveryUsesCapturedReport(bool listenerThrows)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("inner continuation failure");
        ExecutionFailureContext? initial = null;
        using var activities = new QueryActivityProbe(stopping: _ =>
        {
            ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Notification,
                ExecutionCompletion.Committed, ExecutionRecoveryActions.None, transaction.TransactionID, [],
                operation: ExecutionOperationKind.Commit, providerInstanceId: fixture.Provider.TelemetryInstanceId));
            if (listenerThrows) throw primary;
        });
        var native = new ControlledAsyncDatabaseAccess { Reader = new([1]), FailureEvidence = TrustedScalarRead };
        var commands = new ControlledOwnedCommandFactory();
        var input = new AsyncReaderInvocation<int>(new OwnedCommandExecution(native, commands), row => row.GetInt32(0), transaction,
            Identity: new(ExecutionOperationKind.Query, fixture.Provider.TelemetryInstanceId),
            Telemetry: QueryTelemetryContext.Capture(transaction, fixture.RowTable.DbName));
        var transformed = AsyncReaderTransform.Capture<int, int>(input, (_, _) =>
        {
            initial = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.RowLoading,
                ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose,
                transaction.TransactionID, [], operation: ExecutionOperationKind.KeyLookup, providerInstanceId: fixture.Provider.TelemetryInstanceId);
            ExecutionFailureContexts.Attach(primary, initial);
            throw primary;
        });
        var failure = await AsyncEnumerationFailureOf(() => PlanRows(new AsyncReaderEnumerable<int>(() => transformed)));
        var context = transaction.AsyncFailureContext!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.KeyLookup);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(initial!.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(0);
        await Assert.That(native.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(commands.Resource.AsyncDisposals).IsEqualTo(1);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    private static async Task AssertMaterializationOccurrence(Exception failure, ExecutionFailureContext context,
        SettledFailureOccurrence occurrence, bool freshReport, ExecutionOperationKind operation = ExecutionOperationKind.Query, bool canContinue = true)
    {
        await Assert.That(failure).IsSameReferenceAs(occurrence.Reused);
        await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : operation);
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        // Current nested reports keep their own recovery; local failures use the
        // actual source's trusted assessment after its resources have settled.
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(canContinue);
        await occurrence.AssertEarlier();
    }

    private sealed class OccurrenceBuffer(Func<IAsyncDataReader, int> materialize, Func<IReadOnlyList<int>>? complete = null) : IAsyncReaderBuffer<int>
    {
        private readonly List<int> rows = [];
        internal bool Completed { get; private set; }
        public void AddRow(IAsyncDataReader reader) => rows.Add(materialize(reader));
        public IReadOnlyList<int> Complete(CancellationToken token) { Completed = true; return complete?.Invoke() ?? rows; }
    }
}
