using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Linq;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task HelperLifetime_RejectsBorrowedCompletionButAllowsSequentialManagedWork()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledHelperTransaction { DisposingTransaction = transaction.DatabaseAccess.Dispose };
        var result = await TransactionCallbackRunner.RunAsync(transaction.ExecutionGate, resource, new(), transaction.TransactionID,
            async token =>
            {
                _ = Capture<InvalidOperationException>(transaction.Commit);
                _ = Capture<InvalidOperationException>(transaction.Rollback);
                _ = Capture<InvalidOperationException>(transaction.Dispose);
                transaction.Delete(fixture.CreateImmutable(401, "first"));
                await Task.Yield();
                await NestedService();
                return 23;
            });
        await Assert.That(result).IsEqualTo(23);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(2);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.Query());

        async Task NestedService()
        {
            await Task.Yield();
            transaction.Delete(fixture.CreateImmutable(402, "second"));
            _ = Capture<InvalidOperationException>(transaction.Commit);
        }
    }

    [Test]
    [Arguments("between-rows")]
    [Arguments("pending-move")]
    [Arguments("pending-cleanup")]
    public async Task HelperLifetime_DrainsEscapedAsyncReaderBeforeRollback(string phase)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var resource = new ControlledHelperTransaction { DisposingTransaction = transaction.DatabaseAccess.Dispose };
        var clock = new ControlledRecoveryTimeProvider();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Cleanup = new(paused: true);
        if (phase == "pending-move") access.Reader.Advance = new(paused: true);
        var rows = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction).GetAsyncEnumerator();
        Task? escaped = null;
        var pending = TransactionCallbackRunner.RunAsync(transaction.ExecutionGate, resource, new(), transaction.TransactionID,
            async token =>
            {
                var move = rows.MoveNextAsync().AsTask();
                if (phase == "pending-move")
                {
                    escaped = move;
                    await access.Reader.Advance.Entered;
                }
                else
                {
                    if (!await move) throw new Exception("Expected a row.");
                    if (phase == "pending-cleanup") escaped = rows.DisposeAsync().AsTask();
                }
                return 23;
            }, timeProvider: clock);
        try
        {
            if (phase == "pending-move")
            {
                await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(clock.Created).IsEqualTo(0);
                access.Reader.Advance.Release();
            }
            await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(resource.Calls).IsEmpty();
            await Assert.That(clock.Created).IsEqualTo(0);
            _ = Capture<InvalidOperationException>(() => rows.MoveNextAsync());
            _ = Capture<InvalidOperationException>(() => rows.DisposeAsync());
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { access.Reader.Advance.Release(); access.Reader.Cleanup.Release(); }
        var error = await AsyncEnumerationFailureOf(() => pending);
        if (escaped is not null) await escaped.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await rows.DisposeAsync();
        _ = Capture<InvalidOperationException>(() => rows.MoveNextAsync());
    }

    [Test]
    public async Task HelperLifetime_PreservesCallbackAndPendingReadFailuresWithoutDuplicates()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var callbackFailure = new FormatException("callback");
        var readFailure = new InvalidOperationException("read");
        var cleanupFailure = new Exception("reader cleanup");
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Advance = new(paused: true);
        access.Reader.Cleanup = new(paused: true);
        access.Reader.Cleanup.Fail(cleanupFailure);
        var rows = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction).GetAsyncEnumerator();
        Task? move = null;
        var resource = new ControlledHelperTransaction
        {
            Recovery = ExecutionRecoveryActions.Dispose,
            DisposingTransaction = transaction.DatabaseAccess.Dispose
        };
        var pending = TransactionCallbackRunner.RunAsync<int>(transaction.ExecutionGate, resource, new(), transaction.TransactionID,
            async token =>
            {
                move = rows.MoveNextAsync().AsTask();
                await access.Reader.Advance.Entered;
                throw callbackFailure;
            });
        await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        access.Reader.Advance.Fail(readFailure);
        await Assert.That(await AsyncEnumerationFailureOf(() => move!)).IsSameReferenceAs(readFailure);
        var error = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(error).IsSameReferenceAs(callbackFailure);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(3);
        await Assert.That(context.SecondaryFailures[0].Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(readFailure);
        await Assert.That(context.SecondaryFailures[2].Exception).IsSameReferenceAs(cleanupFailure);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(resource.Calls.Contains("rollback")).IsFalse();
        await rows.DisposeAsync();
    }

    [Test]
    [Arguments("linq")]
    [Arguments("fluent")]
    [Arguments("raw")]
    public async Task HelperLifetime_ClosesEscapedSynchronousReader(string route)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        fixture.Scenario.ReaderFactory = () => reader;
        IEnumerator<TransactionMutationGuardRow>? escaped = null;
        var resource = new ControlledHelperTransaction { DisposingTransaction = transaction.DatabaseAccess.Dispose };
        var pending = TransactionCallbackRunner.RunAsync(transaction.ExecutionGate, resource, new(), transaction.TransactionID,
            token =>
            {
                IEnumerable<TransactionMutationGuardRow> query = route switch
                {
                    "linq" => transaction.Query().Rows.Where(row => row.Id == 401),
                    "fluent" => transaction.From<TransactionMutationGuardRow>().Where("id").EqualTo(401).SelectQuery().ExecuteAs<TransactionMutationGuardRow>(),
                    _ => transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows")
                };
                escaped = query.GetEnumerator();
                if (!escaped.MoveNext()) throw new Exception("Expected a row.");
                return Task.FromResult(23);
            });
        var error = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        _ = Capture<InvalidOperationException>(() => escaped!.MoveNext());
        escaped!.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HelperLifetime_WaitsForInFlightSynchronousReadAndRetainsItsFailure(bool failRead)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var resume = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new FormatException("synchronous reader failure");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        reader.OnRead = () =>
        {
            entered.TrySetResult();
            if (!resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release the reader.");
            if (failRead) throw expected;
        };
        fixture.Scenario.ReaderFactory = () => reader;
        var resource = new ControlledHelperTransaction { DisposingTransaction = transaction.DatabaseAccess.Dispose };
        var rows = transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows").GetEnumerator();
        var lifetime = transaction.ExecutionGate.BeginHelperLifetime();
        // Test-only worker runs the synchronous provider; production has no Task.Run facade.
        var move = Task.Run(rows.MoveNext);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var failures = new ExecutionFailures();
        var draining = lifetime.CloseAndDrainAsync(failures); // Closure is synchronous and atomic.
        var pending = Recover();
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(resource.Calls).IsEmpty();
            await Assert.That(reader.Disposals).IsEqualTo(0);
        }
        finally { resume.Set(); }
        if (failRead) await Assert.That(await AsyncEnumerationFailureOf(() => move!)).IsSameReferenceAs(expected);
        else await Assert.That(await move!.WaitAsync(TimeSpan.FromSeconds(10))).IsTrue();
        var error = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(failRead ? 1 : 0);
        if (failRead) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(expected);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        rows.Dispose();

        async Task Recover()
        {
            using var owner = await draining;
            var cleanup = new AutomaticTransactionRecovery(transaction.ExecutionGate, owner, resource, new(), failures,
                ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Rollback, transaction.TransactionID);
            await cleanup.DisposeAsync();
        }
    }
}
