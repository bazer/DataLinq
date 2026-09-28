using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class StandaloneCompletionTests
{
    [Test]
    public async Task SuspendedCommitAndCleanupRejectAllCompetingCompletion()
    {
        var completion = new ControlledCompletionProvider { Commit = new(paused: true), ConnectionCleanup = new(paused: true) };
        var transaction = new Probe(completion);
        var pending = transaction.CommitAsync();
        try
        {
            await completion.Commit.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await RejectCompeting();
            completion.Commit.Release();
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
            await Assert.That(pending.IsCompleted).IsFalse();
            await RejectCompeting();
        }
        finally { completion.Commit.Release(); completion.ConnectionCleanup.Release(); }
        await pending;
        await transaction.DisposeAsync();
        transaction.Dispose();
        await Assert.That(transaction.SyncCalls).IsEqualTo(0);
        await Assert.That(completion.Calls.SequenceEqual(["commit", "dispose-transaction", "dispose-connection"])).IsTrue();

        async Task RejectCompeting()
        {
            await Assert.That(() => transaction.CommitAsync()).Throws<InvalidOperationException>();
            await Assert.That(() => transaction.RollbackAsync()).Throws<InvalidOperationException>();
            await Assert.That(async () => await transaction.DisposeAsync()).Throws<InvalidOperationException>();
            await Assert.That(transaction.Commit).Throws<InvalidOperationException>();
            await Assert.That(transaction.Rollback).Throws<InvalidOperationException>();
            await Assert.That(transaction.Dispose).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task FailedCommitIsNotRetriedAndConfirmedRecoveryDoesNotInventCertainty()
    {
        var completion = new ControlledCompletionProvider { Commit = new(paused: true) };
        var expected = new Exception("lost confirmation");
        completion.Commit.Fail(expected);
        var transaction = new Probe(completion);
        var error = await Assert.That(() => transaction.CommitAsync()).Throws<Exception>();
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(() => transaction.CommitAsync()).Throws<InvalidOperationException>();
        await Assert.That(transaction.Commit).Throws<InvalidOperationException>();
        await transaction.RollbackAsync();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(transaction.SynchronousCompletion).IsEqualTo(ExecutionCompletion.Unknown);
        await transaction.DisposeAsync();
        await Assert.That(completion.Calls.SequenceEqual(["commit", "rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
    }

    [Test]
    public async Task FailedRollbackIsNotRetriedAndBothCleanupFailuresRemainOrdered()
    {
        var completion = new ControlledCompletionProvider
        {
            Rollback = new(paused: true), TransactionCleanup = new(paused: true), ConnectionCleanup = new(paused: true)
        };
        var rollback = new Exception("rollback");
        var first = new Exception("transaction cleanup");
        var second = new Exception("connection cleanup");
        completion.Rollback.Fail(rollback);
        completion.TransactionCleanup.Fail(first);
        completion.ConnectionCleanup.Fail(second);
        var transaction = new Probe(completion);
        await Assert.That(await Assert.That(() => transaction.RollbackAsync()).Throws<Exception>()).IsSameReferenceAs(rollback);
        var error = await Assert.That(async () => await transaction.DisposeAsync()).Throws<Exception>();
        await Assert.That(error).IsSameReferenceAs(first);
        var context = ExecutionFailureContexts.Get(first)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(second);
        await transaction.DisposeAsync();
        transaction.Dispose();
        await Assert.That(completion.Calls.Count(value => value == "rollback")).IsEqualTo(1);
    }

    [Test]
    public async Task DisposalKeepsRollbackFailurePrimaryThroughBothCleanupAttempts()
    {
        var completion = new ControlledCompletionProvider
        {
            Rollback = new(paused: true), TransactionCleanup = new(paused: true), ConnectionCleanup = new(paused: true)
        };
        var expected = new Exception("rollback");
        var cleanup = new Exception("cleanup");
        completion.Rollback.Fail(expected);
        completion.TransactionCleanup.Fail(cleanup);
        completion.ConnectionCleanup.Release();
        var transaction = new Probe(completion);
        var error = await Assert.That(async () => await transaction.DisposeAsync()).Throws<Exception>();
        await Assert.That(error).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(completion.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
    }

    private sealed class Probe(ControlledCompletionProvider completion) : Legacy, ISyncTransactionCompletionResource
    {
        public override Task CommitAsync(CancellationToken cancellationToken = default) => CompleteStandaloneAsync(completion, false, cancellationToken);
        public override Task RollbackAsync(CancellationToken cancellationToken = default) => CompleteStandaloneAsync(completion, true, cancellationToken);
        public override ValueTask DisposeAsync() => DisposeStandaloneAsync(completion);
        public override void Commit() => CompleteSynchronousTransaction(this, false);
        public override void Rollback() => CompleteSynchronousTransaction(this, true);
        public override void Dispose() => DisposeSynchronousTransaction(this);
        public void ValidateCompletion() { }
        public void Complete(bool rollback) => SyncCalls++;
        public bool RollbackForDisposal() { SyncCalls++; return true; }
        public void CloseConnection() => SyncCalls++;
        public void DisposeConnection() => SyncCalls++;
        public void DisposeTransaction() => SyncCalls++;
    }

    [Test]
    public async Task LegacySubclassAsyncDefaultsNeverCallSynchronousMethods()
    {
        var legacy = new Legacy();
        await Assert.That(() => legacy.CommitAsync()).Throws<NotSupportedException>();
        await Assert.That(() => legacy.RollbackAsync()).Throws<NotSupportedException>();
        await Assert.That(async () => await ((IAsyncDisposable)legacy).DisposeAsync()).Throws<NotSupportedException>();
        await Assert.That(legacy.SyncCalls).IsEqualTo(0);
    }

    private class Legacy : DatabaseTransaction
    {
        internal int SyncCalls;
        internal Legacy() : base(TransactionType.ReadAndWrite) => SetStatus(DatabaseTransactionStatus.Open);
        public override void Commit() => SyncCalls++;
        public override void Rollback() => SyncCalls++;
        public override void Dispose() => SyncCalls++;
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
    }
}
