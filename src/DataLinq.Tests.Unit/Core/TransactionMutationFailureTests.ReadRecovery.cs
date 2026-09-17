using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("trusted-read", 7)]
    [Arguments("unknown-read", 4)]
    [Arguments("rollback-only", 6)]
    [Arguments("raw-command", 6)]
    [Arguments("mutation", 6)]
    [Arguments("initialization", 4)]
    [Arguments("lost", 4)]
    [Arguments("cleanup-failure", 4)]
    public async Task AsyncReadRecovery_EnforcesEvidenceBeforeNextOperation(string mode, int actions)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var access = RecoveryAccess(mode);
        var expected = new FormatException("provider statement");
        access.Dispatch.Fail(expected);
        // Cleanup failure needs a reader to have been transferred before the failure.
        if (mode == "cleanup-failure")
        {
            access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedReadEvidence() };
            access.Reader.Advance = new AsyncCheckpoint(paused: true);
            access.Reader.Advance.Fail(expected);
            access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
            access.Reader.Cleanup.Fail(new Exception("cleanup failed"));
        }
        access.AssessingFailure = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        };
        await using var rows = RecoverySequence(access, command, transaction).GetAsyncEnumerator();
        var reported = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        await Assert.That(reported).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(reported)!;
        await Assert.That(context).IsSameReferenceAs(transaction.AsyncFailureContext);
        await Assert.That((int)context.Recovery).IsEqualTo(actions);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        if (mode == "initialization")
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        if ((actions & 1) != 0)
        {
            _ = transaction.Query();
            transaction.EnsureMutationNotPoisoned(TransactionChangeType.Insert);
        }
        else
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => transaction.EnsureMutationNotPoisoned(TransactionChangeType.Insert));
            _ = Capture<InvalidOperationException>(() => transaction.Delete(fixture.CreateImmutable(401, "unchanged")));
            await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
            _ = Capture<InvalidOperationException>(transaction.Commit);
            var callsBefore = access.Calls.Count;
            await using var later = RecoverySequence(access, command, transaction).GetAsyncEnumerator();
            await Assert.That(await AsyncEnumerationFailureOf(() => later.MoveNextAsync().AsTask())).IsTypeOf<InvalidOperationException>();
            await Assert.That(access.Calls.Count).IsEqualTo(callsBefore);
            await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
        }
        if ((actions & 2) != 0)
        {
            transaction.Rollback();
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        }
        else
            _ = Capture<InvalidOperationException>(transaction.Rollback);
        transaction.Dispose();
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(ExecutionFailureContexts.Get(reported)).IsSameReferenceAs(context);
        await Assert.That((int)context.Recovery).IsEqualTo(actions);
    }

    [Test]
    public async Task AsyncReadRecovery_CleanupAndAssessmentFailuresStayOrderedUnderOriginalFailure()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedReadEvidence() };
        var primary = new FormatException("decode");
        var cleanup = new InvalidOperationException("dispose reader");
        var assessment = new NotSupportedException("provider assessment");
        access.Reader.Advance = new AsyncCheckpoint(paused: true);
        access.Reader.Advance.Fail(primary);
        access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
        access.EvidenceFailure = assessment;
        await using var rows = RecoverySequence(access, command, transaction).GetAsyncEnumerator();
        var move = rows.MoveNextAsync().AsTask();
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(transaction.AsyncFailureContext).IsNull();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(move.IsCompleted).IsFalse();
        }
        finally { access.Reader.Cleanup.Fail(cleanup); }
        await Assert.That(await AsyncEnumerationFailureOf(() => move)).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(2);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(assessment);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await rows.DisposeAsync();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(primary.StackTrace!).Contains(nameof(ControlledAsyncDataReader.ReadNextRowAsync));
    }

    [Test]
    public async Task AsyncReadRecovery_CanceledTokenDoesNotRelabelUnrelatedProviderFailure()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var access = RecoveryAccess("trusted-read");
        var primary = new InvalidOperationException("provider timeout with its original code");
        access.FailureEvidence = TrustedReadEvidence() with { Cause = ExecutionFailureCause.Timeout };
        access.Dispatch.Fail(primary);
        access.AssessingFailure = cancellation.Cancel;
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask())).IsSameReferenceAs(primary);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(ExecutionFailureContexts.Get(primary)!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncReadRecovery_KnownLocalCancellationPreservesClassificationAndPriorWork()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        transaction.Delete(fixture.CreateImmutable(402, "prior transaction work"));
        var access = new ControlledAsyncDatabaseAccess { FailureEvidence = TrustedReadEvidence() };
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => { cancellation.Cancel(); return reader.GetInt32(0); }, transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        var primary = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        await Assert.That(primary is OperationCanceledException).IsTrue();
        await Assert.That(ExecutionFailureContexts.Get(primary)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        _ = transaction.Query();
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(0);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        transaction.Commit();
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
        await Assert.That(ExecutionFailureContexts.Get(primary)!.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Committed);
    }

    [Test]
    [Arguments("commit-confirmed")]
    [Arguments("commit-unknown")]
    [Arguments("commit-unknown-after-status")]
    [Arguments("rollback-confirmed")]
    [Arguments("rollback-unknown")]
    public async Task AsyncReadRecovery_LaterCompletionUpdatesOnlyTransactionSnapshot(string completion)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var access = RecoveryAccess("trusted-read");
        var readFailure = new FormatException("original read");
        access.Dispatch.Fail(readFailure);
        await using var rows = RecoverySequence(access, command, transaction).GetAsyncEnumerator();
        _ = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        var before = ExecutionFailureContexts.Get(readFailure)!;
        var failure = new InvalidOperationException("completion/finalization");
        if (completion == "commit-unknown")
        {
            fixture.Scenario.CommitFailureBeforeStatus = failure;
            await Assert.That(Capture<InvalidOperationException>(transaction.Commit)).IsSameReferenceAs(failure);
            fixture.Scenario.CommitFailureBeforeStatus = null;
            _ = Capture<InvalidOperationException>(transaction.Rollback);
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        }
        else if (completion == "commit-unknown-after-status")
        {
            fixture.Scenario.CommitFailureAfterStatus = failure;
            await Assert.That(Capture<InvalidOperationException>(transaction.Commit)).IsSameReferenceAs(failure);
            _ = Capture<InvalidOperationException>(transaction.Rollback);
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        }
        else if (completion == "rollback-unknown")
        {
            fixture.Scenario.RollbackFailure = failure;
            await Assert.That(Capture<InvalidOperationException>(transaction.Rollback)).IsSameReferenceAs(failure);
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        }
        else
        {
            transaction.OnStatusChanged += (_, _) => throw failure;
            if (completion == "commit-confirmed")
                await Assert.That(Capture<InvalidOperationException>(transaction.Commit)).IsSameReferenceAs(failure);
            else
                await Assert.That(Capture<InvalidOperationException>(transaction.Rollback)).IsSameReferenceAs(failure);
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(
                completion == "commit-confirmed" ? ExecutionCompletion.Committed : ExecutionCompletion.RolledBack);
        }
        await Assert.That(before.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(ExecutionFailureContexts.Get(readFailure)).IsSameReferenceAs(before);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AsyncReadRecovery_TerminalSourceFallbackRequiresConfirmedCompletion(bool confirmRollback)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var model = fixture.CreateImmutable(410, "captured", transaction);
        var access = RecoveryAccess(confirmRollback ? "rollback-only" : "unknown-read");
        access.Dispatch.Fail(new InvalidOperationException("interrupted read"));
        await using var rows = RecoverySequence(access, command, transaction).GetAsyncEnumerator();
        _ = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        if (confirmRollback)
            transaction.Rollback();
        transaction.Dispose();
        if (confirmRollback)
            await Assert.That(model.GetReadSource()).IsSameReferenceAs(fixture.Provider.ReadOnlyAccess);
        else
            _ = Capture<InvalidOperationException>(() => model.GetReadSource());
    }

    private static ReadFailureEvidence TrustedReadEvidence() =>
        new(ExecutionFailureCause.ProviderError, ExecutionEffects.OrdinaryRead, TransactionIntegrity.Confirmed, true);

    private static ControlledAsyncDatabaseAccess RecoveryAccess(string mode) =>
        new(new AsyncCheckpoint(paused: true))
        {
            FailureEvidence = mode switch
            {
                "trusted-read" => TrustedReadEvidence(),
                "rollback-only" => TrustedReadEvidence() with { Integrity = TransactionIntegrity.Unknown },
                "raw-command" => TrustedReadEvidence() with { Effects = ExecutionEffects.Unknown },
                "mutation" => TrustedReadEvidence() with { Effects = ExecutionEffects.Mutation },
                "initialization" => TrustedReadEvidence() with { Effects = ExecutionEffects.Initialization },
                "lost" => TrustedReadEvidence() with { Integrity = TransactionIntegrity.Lost },
                _ => new()
            }
        };

    private static AsyncReaderEnumerable<int> RecoverySequence(ControlledAsyncDatabaseAccess access,
        ControlledCommand command, Transaction transaction) =>
        new(() => new BorrowedCommandReaderSource(access, command), reader => reader.GetInt32(0), transaction);
}
