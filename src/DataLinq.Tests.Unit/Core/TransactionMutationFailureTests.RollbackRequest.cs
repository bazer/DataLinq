using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RollbackRequest_BeforeWorkRejectsExecutionAndCommitWithoutDispatch(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { InitializationState = TransactionInitializationState.Unused };
        var transaction = fixture.Database.Transaction();
        var reason = new object();
        await Assert.That(transaction.IsRollbackRequested).IsFalse();
        await Assert.That(() => transaction.RequestRollback<string>(null!)).Throws<ArgumentNullException>();
        await Assert.That(transaction.IsRollbackRequested).IsFalse();
        await Assert.That(transaction.RequestRollback(reason).Reason).IsSameReferenceAs(reason);
        await Assert.That(transaction.IsRollbackRequested).IsTrue();
        await Assert.That(transaction.TryGetRollbackReason<object>(out var original)).IsTrue();
        await Assert.That(original).IsSameReferenceAs(reason);
        await Assert.That(transaction.TryGetRollbackReason<string>(out _)).IsFalse();
        await Assert.That(transaction.RequestRollback("mapped").Reason).IsEqualTo("mapped");
        await Assert.That(transaction.TryGetRollbackReason<object>(out original)).IsTrue();
        await Assert.That(original).IsSameReferenceAs(reason);
        await Assert.That(() => transaction.Query()).Throws<DataLinqRollbackRequestedException>();
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => transaction.CommitAsync())
            : Capture<DataLinqRollbackRequestedException>(transaction.Commit);
        await Assert.That(failure).IsTypeOf<DataLinqRollbackRequestedException>();
        await Assert.That(((DataLinqRollbackRequestedException)failure).Reason).IsSameReferenceAs(reason);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(provider.Calls).IsEmpty();
        if (asynchronous) { await transaction.RollbackAsync(); await transaction.DisposeAsync(); }
        else { transaction.Rollback(); transaction.Dispose(); }
        await Assert.That(() => transaction.RequestRollback("late")).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RollbackRequest_ActiveReaderContinuesButUnusedEnumerationIsRejected(bool requestWhileOpening)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: requestWhileOpening));
        var factory = RawFactory(() => access);
        fixture.Scenario.AsyncSqlReaders = factory;
        var deferred = transaction.DatabaseAccess.ReadReaderAsync("SELECT value");
        await using var unused = deferred.GetAsyncEnumerator();
        var pending = transaction.DatabaseAccess.ExecuteReaderAsync("SELECT value");
        if (requestWhileOpening)
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            try { transaction.RequestRollback("opening"); }
            finally { access.Dispatch.Release(); }
        }
        await using (var reader = await pending)
        {
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            if (!requestWhileOpening) transaction.RequestRollback("between moves");
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            await Assert.That(await reader.ReadNextRowAsync(default)).IsFalse();
            await Assert.That(transaction.IsRollbackRequested).IsTrue();
        }
        await Assert.That(await AsyncEnumerationFailureOf(async () => { await unused.MoveNextAsync(); })).IsTypeOf<DataLinqRollbackRequestedException>();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(1);
        transaction.Rollback();
    }

    [Test]
    public async Task RollbackRequest_PendingReaderMoveAndMutationRetainTheirExistingRights()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using (var transaction = fixture.Database.Transaction())
        {
            var access = new ControlledAsyncDatabaseAccess();
            access.Reader.Advance = new(paused: true);
            fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
            await using (var reader = await transaction.DatabaseAccess.ExecuteReaderAsync("SELECT value"))
            {
                var move = reader.ReadNextRowAsync();
                await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    transaction.RequestRollback("during move");
                    await Assert.That(move.IsCompleted).IsFalse();
                }
                finally { access.Reader.Advance.Release(); }
                await Assert.That(await move).IsTrue();
                await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
            }
            transaction.Rollback();
        }
        using (var transaction = fixture.Database.Transaction())
        {
            var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1 };
            EnableAsyncMutations(fixture, access);
            var mutation = transaction.DeleteAsync(fixture.CreateExistingMutable(724, "pending"));
            await Task.WhenAny(access.Dispatch.Entered, mutation).WaitAsync(TimeSpan.FromSeconds(10));
            if (mutation.IsCompleted) await mutation;
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            try { transaction.RequestRollback("during mutation"); }
            finally { access.Dispatch.Release(); }
            await mutation;
            await Assert.That(transaction.IsPoisoned).IsFalse();
            await Assert.That(transaction.Changes.Count).IsEqualTo(1);
            await Assert.That(() => transaction.Delete(fixture.CreateExistingMutable(725, "new"))).Throws<DataLinqRollbackRequestedException>();
            transaction.Rollback();
        }
    }

    [Test]
    public async Task RollbackRequest_VetoDoesNotAuthorizeAnEscapedReaderOrHideRecoveryFailure()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var access = new ControlledAsyncDatabaseAccess();
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        IAsyncEnumerator<IDataLinqDataReader>? escaped = null;
        var helper = fixture.Database.CommitAsync(async transaction =>
        {
            escaped = transaction.DatabaseAccess.ReadReaderAsync("SELECT value").GetAsyncEnumerator();
            await escaped.MoveNextAsync();
            transaction.RequestRollback("requested");
        });
        var unfinished = await AsyncEnumerationFailureOf(() => helper);
        await Assert.That(unfinished).IsTypeOf<InvalidOperationException>();
        await Assert.That(unfinished.Message).Contains("unfinished");
        await Assert.That(provider.Calls.Contains("commit")).IsFalse();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await escaped!.DisposeAsync();

        var rollbackFailure = new InvalidOperationException("physical rollback failed");
        provider = fixture.Scenario.AsyncCompletion = new() { Rollback = new(paused: true) };
        provider.Rollback.Fail(rollbackFailure);
        var failedRecovery = fixture.Database.CommitAsync(transaction =>
        {
            transaction.Delete(fixture.CreateExistingMutable(726, "write"));
            transaction.RequestRollback("business veto");
            return Task.CompletedTask;
        });
        var veto = await AsyncEnumerationFailureOf(() => failedRecovery);
        await Assert.That(veto).IsTypeOf<DataLinqRollbackRequestedException>();
        var context = DataLinq.Diagnostics.DataLinqFailure.GetContext(veto)!;
        await Assert.That(context.CompletionOutcome).IsEqualTo(DataLinq.Diagnostics.DataLinqCompletionOutcome.Unknown);
        await Assert.That(context.SecondaryFailures.Any(item => ReferenceEquals(item.Exception, rollbackFailure))).IsTrue();
        await Assert.That(provider.Calls.Contains("dispose-connection")).IsTrue();
    }

    [Test]
    [Arguments("commit")]
    [Arguments("rollback")]
    [Arguments("dispose")]
    public async Task RollbackRequest_CompletionAdmissionWinsAndLateRequestThrows(string operation)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(721, "deleted"));
        var pause = operation == "commit" ? provider.Commit = new(paused: true)
            : provider.Rollback = new(paused: true);
        var pending = operation switch
        {
            "commit" => transaction.CommitAsync(),
            "rollback" => transaction.RollbackAsync(),
            _ => transaction.DisposeAsync().AsTask()
        };
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            _ = Capture<InvalidOperationException>(() => transaction.RequestRollback("too late"));
            await Assert.That(transaction.IsRollbackRequested).IsFalse();
        }
        finally { pause.Release(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        _ = Capture<InvalidOperationException>(() => transaction.RequestRollback("terminal"));
        await transaction.DisposeAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RollbackRequest_OwningCallbacksIgnoreReturnedCasesAndOnlyHonorExplicitVeto(bool asynchronous)
    {
        foreach (var request in new[] { false, true })
        {
            using var fixture = new ScriptedFixture();
            var provider = fixture.Scenario.AsyncCompletion = new();
            Transaction? captured = null;
            TransactionResult<string> Body(Transaction transaction)
            {
                captured = transaction;
                transaction.Delete(fixture.CreateExistingMutable(722, "prior write"));
                // A caught invalid-result inspection is not an implicit transaction veto.
                try { _ = default(TransactionResult<string>).IsSuccess; }
                catch (InvalidOperationException) { }
                if (request) transaction.RequestRollback("explicit request");
                return new Rollback<string>("returned failure data");
            }
            async Task<TransactionResult<string>> Run() => asynchronous
                ? await fixture.Database.CommitAsync(transaction => Task.FromResult(Body(transaction)))
                : fixture.Database.Commit(Body);
            if (request)
            {
                var failure = await AsyncEnumerationFailureOf(async () => { await Run(); });
                await Assert.That(failure).IsTypeOf<DataLinqRollbackRequestedException>();
                await Assert.That(((DataLinqRollbackRequestedException)failure).Reason).IsEqualTo("explicit request");
                await Assert.That(provider.Calls.Contains("commit")).IsFalse();
                await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
                if (asynchronous) await Assert.That(provider.Calls.Contains("rollback")).IsTrue();
            }
            else
            {
                var result = await Run();
                await Assert.That(result.IsSuccess).IsFalse();
                if (asynchronous) await Assert.That(provider.Calls.Contains("commit")).IsTrue();
                else await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
            }
            await Assert.That(captured!.IsDisposed).IsTrue();
            _ = Capture<InvalidOperationException>(() => captured.RequestRollback("escaped"));
        }
    }

    [Test]
    public async Task RollbackRequest_ConcurrentRepeatedRequestsPreserveOneOriginalReason()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var reasons = Enumerable.Range(0, 12).Select(_ => new object()).ToArray();
        var requests = reasons.Select(reason => Task.Run(() => transaction.RequestRollback(reason))).ToArray();
        var markers = await Task.WhenAll(requests);
        await Assert.That(transaction.IsRollbackRequested).IsTrue();
        await Assert.That(transaction.TryGetRollbackReason<object>(out var first)).IsTrue();
        await Assert.That(reasons.Contains(first)).IsTrue();
        for (var index = 0; index < reasons.Length; index++)
            await Assert.That(markers[index].Reason).IsSameReferenceAs(reasons[index]);
        transaction.RequestRollback(new object());
        transaction.TryGetRollbackReason<object>(out var retained);
        await Assert.That(retained).IsSameReferenceAs(first);
        transaction.Rollback();
        _ = Capture<InvalidOperationException>(() => transaction.RequestRollback(first!));
    }

    [Test]
    public async Task RollbackRequest_HelperClosureRejectsRequestsBeforeRecoveryFinishes()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { Rollback = new(paused: true) };
        Transaction? captured = null;
        var pending = fixture.Database.CommitAsync(transaction =>
        {
            captured = transaction;
            transaction.Delete(fixture.CreateExistingMutable(723, "pending"));
            transaction.RequestRollback("first");
            return Task.CompletedTask;
        });
        await provider.Rollback.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { _ = Capture<InvalidOperationException>(() => captured!.RequestRollback("late repeated")); }
        finally { provider.Rollback.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<DataLinqRollbackRequestedException>();
        await Assert.That(captured!.TryGetRollbackReason<string>(out var reason)).IsTrue();
        await Assert.That(reason).IsEqualTo("first");
    }
}
