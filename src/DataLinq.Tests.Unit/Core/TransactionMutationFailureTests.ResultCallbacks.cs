using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private sealed record MappedRollback(string Code);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ResultCallback_OnlyExplicitVetoChangesCompletionAndMappingPreservesFirstReason(bool asynchronous, bool veto)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        Transaction? captured = null;
        var mapped = new MappedRollback("mapped");
        TransactionResult<string?, MappedRollback> Callback(IDataLinqSession<TransactionMutationGuardDb> session)
        {
            captured = (Transaction)session;
            session.Delete(fixture.CreateImmutable(821, "deleted"));
            if (veto) session.RequestRollback("original diagnostic reason");
            return new Rollback<MappedRollback>(mapped);
        }
        var result = asynchronous
            ? await factory.ExecuteResultAsync((session, _) => Task.FromResult(Callback(session)))
            : factory.ExecuteResult(Callback);
        await Assert.That(result.Reason).IsSameReferenceAs(mapped);
        await Assert.That(captured!.IsDisposed).IsTrue();
        await Assert.That(captured.TryGetRollbackReason<string>(out var reason)).IsEqualTo(veto);
        if (veto) await Assert.That(reason).IsEqualTo("original diagnostic reason");
        await Assert.That(asynchronous ? provider.Calls.Count(call => call == "commit") : fixture.Scenario.Commits).IsEqualTo(veto ? 0 : 1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ResultCallback_InvalidDefaultThrowsBeforeCommitWithoutManufacturingVetoOrPoison(bool asynchronous, bool veto)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        Transaction? captured = null;
        TransactionResult<int> Callback(IDataLinqSession<TransactionMutationGuardDb> session)
        {
            captured = (Transaction)session;
            session.Delete(fixture.CreateImmutable(822, "deleted"));
            if (veto) session.RequestRollback("veto");
            return default;
        }
        var failure = asynchronous
            ? await AsyncEnumerationFailureOf(() => factory.ExecuteResultAsync(session => Task.FromResult(Callback(session))))
            : Capture<Exception>(() => factory.ExecuteResult(Callback));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure is DataLinqRollbackRequestedException).IsFalse();
        await Assert.That(failure.Message.Contains("uninitialized", StringComparison.Ordinal)).IsTrue();
        await Assert.That(captured!.IsRollbackRequested).IsEqualTo(veto);
        await Assert.That(captured.IsPoisoned).IsFalse();
        await Assert.That(captured.IsDisposed).IsTrue();
        await Assert.That(asynchronous ? provider.Calls.Count(call => call == "commit") : fixture.Scenario.Commits).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResultCallback_NullSuccessIsValidButCannotRepresentAnActualVeto(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        foreach (var veto in new[] { false, true })
        {
            TransactionResult<string?> Callback(IDataLinqSession<TransactionMutationGuardDb> session)
            {
                if (veto) session.RequestRollback("veto without rollback payload");
                return TransactionResult.Success<string?>(null);
            }
            async Task<TransactionResult<string?>> Invoke() => asynchronous
                ? await factory.ExecuteResultAsync((session, _) => Task.FromResult(Callback(session)))
                : factory.ExecuteResult(Callback);
            if (veto)
                await Assert.That(await AsyncEnumerationFailureOf(async () => await Invoke())).IsTypeOf<DataLinqRollbackRequestedException>();
            else
            {
                var result = await Invoke();
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.SuccessValue).IsNull();
            }
        }
    }

    [Test]
    [Arguments(false, "rollback")]
    [Arguments(true, "rollback")]
    [Arguments(false, "observer")]
    [Arguments(true, "observer")]
    [Arguments(true, "cleanup")]
    public async Task ResultCallback_RollbackFinalizationOrCleanupFailureCannotReturnAResult(bool asynchronous, string failingStage)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var expected = new Exception(failingStage);
        if (failingStage == "rollback")
        {
            if (asynchronous) { provider.Rollback = new(paused: true); provider.Rollback.Fail(expected); }
            else fixture.Scenario.DisposeRollbackFailure = expected;
        }
        if (failingStage == "cleanup") { provider.ConnectionCleanup = new(paused: true); provider.ConnectionCleanup.Fail(expected); }
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        TransactionResult<int> Callback(IDataLinqSession<TransactionMutationGuardDb> session)
        {
            session.Delete(fixture.CreateImmutable(823, "deleted"));
            if (failingStage == "observer") ((Transaction)session).OnStatusChanged += (_, _) => throw expected;
            return session.RequestRollback("original");
        }
        var actual = asynchronous
            ? await AsyncEnumerationFailureOf(() => factory.ExecuteResultAsync(session => Task.FromResult(Callback(session))))
            : Capture<Exception>(() => factory.ExecuteResult(Callback));
        await Assert.That(actual).IsTypeOf<DataLinqRollbackRequestedException>();
        await Assert.That(((DataLinqRollbackRequestedException)actual).ConfirmedHelperTransactionId).IsNull();
        var context = DataLinqFailure.GetContext(actual)!;
        await Assert.That(context.SecondaryFailures.Any(failure => ReferenceEquals(failure.Exception, expected))).IsTrue();
        await Assert.That(context.CompletionOutcome).IsEqualTo(failingStage == "rollback" ? DataLinqCompletionOutcome.Unknown : DataLinqCompletionOutcome.RolledBack);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResultCallback_NotAttemptedRequiresActualProofThatProviderWorkNeverStarted(bool unused)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new()
        {
            InitializationState = unused ? TransactionInitializationState.Unused : TransactionInitializationState.Ready,
            RecoveryActions = ExecutionRecoveryActions.Dispose
        };
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        Task<TransactionResult<int>> Invoke() => factory.ExecuteResultAsync(session =>
            Task.FromResult((TransactionResult<int>)session.RequestRollback("no rollback available")));
        if (unused) await Assert.That((await Invoke()).Reason).IsEqualTo("no rollback available");
        else
        {
            var failure = await AsyncEnumerationFailureOf(Invoke);
            await Assert.That(failure).IsTypeOf<DataLinqRollbackRequestedException>();
            await Assert.That(DataLinqFailure.GetContext(failure)!.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.NotAttempted);
        }
        await Assert.That(provider.Calls.Contains("rollback")).IsFalse();
        await Assert.That(provider.Calls.Contains("commit")).IsFalse();
    }

    [Test]
    public async Task ResultCallback_DoesNotConvertAConfirmedVetoThrownByTheCallback()
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        var original = Capture<DataLinqRollbackRequestedException>(() => factory.Execute(session => { session.RequestRollback("inner"); return 3; }));
        await Assert.That(original.ConfirmedHelperTransactionId).IsNotNull();
        var actual = await AsyncEnumerationFailureOf(() => factory.ExecuteResultAsync<TransactionMutationGuardDb, int>(session =>
        {
            session.RequestRollback("outer");
            throw original;
        }));
        await Assert.That(actual).IsSameReferenceAs(original);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResultCallback_CaughtMutationFailureCannotBecomeANormalBusinessRollback(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        fixture.Scenario.EnqueueNonQueryFailure(new InjectedMutationException("write failed"));
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        TransactionResult<int> Callback(IDataLinqSession<TransactionMutationGuardDb> session)
        {
            _ = Capture<InjectedMutationException>(() => session.Delete(fixture.CreateExistingMutable(824, "failed")));
            return session.RequestRollback("business mapping must not hide a poisoned transaction");
        }
        var failure = asynchronous
            ? await AsyncEnumerationFailureOf(() => factory.ExecuteResultAsync(session => Task.FromResult(Callback(session))))
            : Capture<Exception>(() => factory.ExecuteResult(Callback));
        await Assert.That(failure).IsTypeOf<DataLinqRollbackRequestedException>();
        await Assert.That(((DataLinqRollbackRequestedException)failure).ConfirmedHelperTransactionId).IsNull();
        await Assert.That(DataLinqFailure.GetContext(failure)!.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.RolledBack);
    }

    [Test]
    public async Task ResultCallback_FailedInitializationCannotBeReportedAsNoStartedWork()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        var failure = await AsyncEnumerationFailureOf(() => factory.ExecuteResultAsync(session =>
        {
            provider.InitializationState = TransactionInitializationState.Failed;
            return Task.FromResult((TransactionResult<int>)session.RequestRollback("failed initialization"));
        }));
        await Assert.That(failure).IsTypeOf<DataLinqRollbackRequestedException>();
        await Assert.That(((DataLinqRollbackRequestedException)failure).ConfirmedHelperTransactionId).IsNull();
        await Assert.That(DataLinqFailure.GetContext(failure)!.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.NotAttempted);
        await Assert.That(provider.Calls).IsEquivalentTo(new[] { "dispose-transaction", "dispose-connection" });
    }

    [Test]
    public async Task ResultCallback_WaitsForRecoveryAndIndependentCleanupDespiteRequestCancellation()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { Rollback = new(paused: true), ConnectionCleanup = new(paused: true) };
        using var request = new CancellationTokenSource();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        var pending = factory.ExecuteResultAsync((session, token) =>
        {
            if (token != request.Token) throw new Exception("Request token changed.");
            request.Cancel();
            return Task.FromResult((TransactionResult<DataLinq.Mutation.Unit>)session.RequestRollback("business veto"));
        }, cancellationToken: request.Token);
        await provider.Rollback.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(provider.RollbackToken == request.Token).IsFalse();
            await Assert.That(provider.RollbackToken.IsCancellationRequested).IsFalse();
        }
        finally { provider.Rollback.Release(); }
        await provider.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { await Assert.That(pending.IsCompleted).IsFalse(); }
        finally { provider.ConnectionCleanup.Release(); }
        await Assert.That((await pending).Reason).IsEqualTo("business veto");
    }
}
