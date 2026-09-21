using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("sync-raw", false)]
    [Arguments("sync-raw", true)]
    [Arguments("async-raw", false)]
    [Arguments("async-raw", true)]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("buffered", false)]
    [Arguments("buffered", true)]
    public async Task SettledOccurrences_PostCleanupConversionOwnsItsFailure(string kind, bool freshReport)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        int Convert()
        {
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        }
        var native = new ControlledAsyncDatabaseAccess
        {
            ScalarResult = 7, Reader = new([]), FailureEvidence = TrustedScalarRead
        };
        var eager = new ControlledEagerCommandFactory
        {
            Access = native, ScalarConverting = _ => Convert(),
            ConfigureCommand = command => command.Resource.Disposing = occurrence.RecordEarlier
        };
        fixture.Scenario.AsyncCommands = eager;
        var scalars = RawFactory(() => native);
        scalars.ConfigureCommand = command => command.Resource.Disposing = occurrence.RecordEarlier;
        scalars.ScalarConverting = _ => Convert();
        fixture.Scenario.AsyncSqlScalars = scalars;
        var sync = EnableSyncRaw(fixture);
        sync.CommandDisposing = occurrence.RecordEarlier;
        sync.Converting = () => { _ = Convert(); };
        var owned = new ControlledOwnedCommandFactory();
        owned.Resource.Disposing = occurrence.RecordEarlier;
        var operation = kind switch
        {
            "scalar" => ExecutionOperationKind.Query, "buffered" => ExecutionOperationKind.KeyLookup,
            _ => ExecutionOperationKind.RawCommand
        };
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            switch (kind)
            {
                case "sync-raw": _ = transaction.DatabaseAccess.ExecuteScalar<int>("SELECT value"); break;
                case "async-raw": _ = await transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>("SELECT value"); break;
                case "scalar": _ = await transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore<int>(); break;
                default: _ = await new AsyncBufferedRead<int>(transaction, new OwnedCommandExecution(native, owned),
                    _ => { }, Convert, operationKind: operation).ExecuteAsync(default); break;
            }
        });
        await Assert.That(failure).IsSameReferenceAs(occurrence.Reused);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : operation);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(kind is "scalar" or "buffered");
        var disposals = sync.CommandDisposals + eager.Commands.Sum(x => x.Resource.AsyncDisposals) +
            scalars.Commands.Sum(x => x.Resource.AsyncDisposals) + owned.Resource.AsyncDisposals;
        await Assert.That(disposals).IsEqualTo(1);
        await occurrence.AssertEarlier();
    }

    [Test]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("buffered", false)]
    [Arguments("buffered", true)]
    [Arguments("save", false)]
    [Arguments("save", true)]
    [Arguments("delete", false)]
    [Arguments("delete", true)]
    [Arguments("unchanged-save", false)]
    [Arguments("unchanged-save", true)]
    public async Task SettledOccurrences_AssessmentCannotReuseEarlierWorkReport(string kind, bool freshReport)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var work = new AsyncCheckpoint(paused: true) { ReportingFailure = _ => occurrence.RecordEarlier() };
        work.Fail(occurrence.Primary);
        var native = new ControlledAsyncDatabaseAccess(work)
        {
            EvidenceFailure = occurrence.Reused,
            AssessingFailure = () => { if (freshReport) occurrence.ReportCurrent(); }
        };
        var mutations = EnableAsyncMutations(fixture, native);
        var readers = RawFactory(() => native);
        fixture.Scenario.AsyncSqlReaders = readers;
        fixture.Scenario.AsyncSqlScalars = readers;
        var mutable = fixture.CreateExistingMutable(1, "original");
        if (kind == "save") mutable["Value"] = "changed";
        var operation = kind switch
        {
            "scalar" => ExecutionOperationKind.Query, "buffered" => ExecutionOperationKind.KeyLookup,
            "delete" => ExecutionOperationKind.Delete, _ => ExecutionOperationKind.Save
        };
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            switch (kind)
            {
                case "scalar": await transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore(); break;
                case "buffered": await new DataSourceAccessSourceRowLoader(transaction, asyncOperationKind: operation)
                        .LoadSingleAsync(fixture.RowTable, DataLinqKey.FromValue(1)); break;
                case "delete": await transaction.DeleteAsyncCore(mutable); break;
                default: await transaction.SaveAsyncCore(mutable); break;
            }
        });
        await Assert.That(failure).IsSameReferenceAs(occurrence.Primary);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await occurrence.AssertSecondary(context, freshReport, cleanup: false, operation);
        await Assert.That(context.Operation).IsEqualTo(operation);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(kind == "unchanged-save" ? 2 : 1);
        await Assert.That(mutations.Commands.Sum(x => x.Resource.AsyncDisposals) + readers.Commands.Sum(x => x.Resource.AsyncDisposals)).IsEqualTo(1);
        await Assert.That(transaction.IsPoisoned).IsEqualTo(kind is "save" or "delete");
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SettledOccurrences_RelationCompletionOwnsAssessment(bool freshReport)
    {
        using var fixture = new AsyncRelationFixture(reference: true);
        using var transaction = fixture.Provider.StartTransaction();
        var occurrence = new SettledFailureOccurrence();
        var native = fixture.SetRows();
        native.EvidenceFailure = occurrence.Reused;
        native.AssessingFailure = () => { if (freshReport) occurrence.ReportCurrent(); };
        fixture.Factory.ConfigureCommand = command => command.Resource.Disposing = occurrence.RecordEarlier;
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationChild)).Model.RelationProperties[nameof(AsyncRelationChild.Parent)];
        Exception failure;
        using (var ownership = DataSourceAccess.BeginRead(transaction, "complete captured relation", operationKind: ExecutionOperationKind.RelationLoad))
        {
            var prepared = fixture.Cache.PrepareRelationRowsAsyncCore(1, property, transaction, ownership!.Step);
            failure = await AsyncEnumerationFailureOf(() =>
                fixture.Cache.ExecuteRelationRowsAsyncCore<int>(prepared, _ => throw occurrence.Primary, default));
        }
        await Assert.That(failure).IsSameReferenceAs(occurrence.Primary);
        var context = transaction.AsyncFailureContext!;
        await occurrence.AssertSecondary(context, freshReport, cleanup: false, ExecutionOperationKind.RelationLoad);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RelationLoad);
        await Assert.That(native.Calls.Count(x => x == "assess-failure")).IsEqualTo(1);
        await Assert.That(fixture.Factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        _ = Capture<InvalidOperationException>(() => transaction.EnsureCanRead("read after failed relation completion"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SettledOccurrences_BufferedBorrowedReaderOwnsCleanup(bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var advance = new AsyncCheckpoint(paused: true) { ReportingFailure = _ => occurrence.RecordEarlier() };
        advance.Fail(occurrence.Primary);
        var cleanup = new AsyncCheckpoint(paused: true)
        {
            ReportingFailure = _ => { if (freshReport) occurrence.ReportCurrent(); }
        };
        cleanup.Fail(occurrence.Reused);
        var reader = new ControlledAsyncDataReader { Advance = advance, Cleanup = cleanup };
        var native = new ControlledAsyncDatabaseAccess { Reader = reader, FailureEvidence = TrustedScalarRead };
        using var command = new ControlledCommand();
        var source = new BorrowedCommandReaderSource(native, command);
        var read = new AsyncBufferedRead<int>(transaction, source, _ => { }, () => 0, operationKind: ExecutionOperationKind.KeyLookup);
        var failure = await AsyncEnumerationFailureOf(() => read.ExecuteAsync(default));
        await Assert.That(failure).IsSameReferenceAs(occurrence.Primary);
        await occurrence.AssertSecondary(transaction.AsyncFailureContext!, freshReport, cleanup: true, ExecutionOperationKind.KeyLookup);
        await Assert.That(reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    private sealed class SettledFailureOccurrence
    {
        internal Exception Primary { get; } = new("work");
        internal Exception Reused { get; } = new("reused during later work");
        private Exception EarlierSecondary { get; } = new("earlier cleanup");
        private ExecutionFailureContext? earlier;

        internal void RecordEarlier()
        {
            using var nested = ExecutionFailureScope.Begin();
            earlier = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
                [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, EarlierSecondary)],
                operation: ExecutionOperationKind.Commit);
            ExecutionFailureContexts.Attach(Reused, earlier);
        }

        internal void ReportCurrent() => ExecutionFailureContexts.Attach(Reused, new(
            ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [],
            operation: ExecutionOperationKind.Rollback));

        internal async Task AssertSecondary(ExecutionFailureContext context, bool freshReport, bool cleanup, ExecutionOperationKind operation)
        {
            await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
            var secondary = context.SecondaryFailures.Single();
            await Assert.That(secondary.Exception).IsSameReferenceAs(Reused);
            await Assert.That(secondary.Cause).IsEqualTo(cleanup && freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
            await Assert.That(secondary.Stage).IsEqualTo(cleanup ? ExecutionFailureStage.Cleanup : ExecutionFailureStage.Recovery);
            await Assert.That(secondary.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback :
                cleanup ? ExecutionOperationKind.Dispose : operation);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
            await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanup);
            await AssertEarlier();
        }

        internal async Task AssertEarlier()
        {
            await Assert.That(earlier!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
            await Assert.That(earlier.Operation).IsEqualTo(ExecutionOperationKind.Commit);
            await Assert.That(earlier.SecondaryFailures.Single().Exception).IsSameReferenceAs(EarlierSecondary);
        }
    }
}
