using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("database", 0)]
    [Arguments("database", 1)]
    [Arguments("database", 2)]
    [Arguments("database", 3)]
    [Arguments("base", 0)]
    [Arguments("base", 1)]
    [Arguments("base", 2)]
    [Arguments("base", 3)]
    [Arguments("interface", 0)]
    [Arguments("interface", 1)]
    [Arguments("interface", 2)]
    [Arguments("interface", 3)]
    public async Task PublicLifecycle_AllCallbackShapesWaitForCleanup(string receiver, int shape)
    {
        using var fixture = new ScriptedFixture();
        var completion = fixture.Scenario.AsyncCompletion = new() { ConnectionCleanup = new(paused: true) };
        using var request = new CancellationTokenSource();
        var calls = 0;
        Transaction? captured = null;
        Task<int> Callback(Transaction transaction, CancellationToken token)
        {
            calls++;
            captured = transaction;
            if (shape % 2 == 1 && token != request.Token) throw new Exception("Token was not forwarded.");
            return Task.FromResult(42);
        }
        var type = TransactionType.ReadOnly;
        Task task;
        if (receiver == "database")
            task = shape switch
            {
                0 => fixture.Database.CommitAsync(tx => (Task)Callback(tx, default), type, request.Token),
                1 => fixture.Database.CommitAsync((tx, ct) => (Task)Callback(tx, ct), type, request.Token),
                2 => fixture.Database.CommitAsync(tx => Callback(tx, default), type, request.Token),
                _ => fixture.Database.CommitAsync((tx, ct) => Callback(tx, ct), type, request.Token)
            };
        else if (receiver == "base")
        {
            DatabaseProvider provider = fixture.Provider;
            task = shape switch
            {
                0 => provider.CommitAsync(tx => (Task)Callback(tx, default), type, request.Token),
                1 => provider.CommitAsync((tx, ct) => (Task)Callback(tx, ct), type, request.Token),
                2 => provider.CommitAsync(tx => Callback(tx, default), type, request.Token),
                _ => provider.CommitAsync((tx, ct) => Callback(tx, ct), type, request.Token)
            };
        }
        else
        {
            IDatabaseProvider provider = fixture.Provider;
            task = shape switch
            {
                0 => provider.CommitAsync(tx => (Task)Callback(tx, default), type, request.Token),
                1 => provider.CommitAsync((tx, ct) => (Task)Callback(tx, ct), type, request.Token),
                2 => provider.CommitAsync(tx => Callback(tx, default), type, request.Token),
                _ => provider.CommitAsync((tx, ct) => Callback(tx, ct), type, request.Token)
            };
        }
        try
        {
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(task.IsCompleted).IsFalse();
            await Assert.That(calls).IsEqualTo(1);
            await Assert.That(captured!.IsDisposed).IsTrue();
            await Assert.That(captured.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        }
        finally { completion.ConnectionCleanup.Release(); }
        await task;
        if (shape >= 2) await Assert.That(await (Task<int>)task).IsEqualTo(42);
        await Assert.That(fixture.Scenario.CreatedTransactionTypes.Single()).IsEqualTo(type);
        await Assert.That(completion.Calls.SequenceEqual(["commit", "dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
    }

    [Test]
    [Arguments("throw")]
    [Arguments("null-task")]
    [Arguments("canceled")]
    [Arguments("validation")]
    public async Task PublicLifecycle_CallbackFailuresRetainOwnershipAndOriginalException(string kind)
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("callback or validation");
        var completion = fixture.Scenario.AsyncCompletion = new();
        if (kind == "validation") completion.ValidationFailure = expected;
        var calls = 0;
        var error = await AsyncEnumerationFailureOf(() => fixture.Database.CommitAsync<int>((_, ct) =>
        {
            calls++;
            return kind == "null-task" ? null! : throw expected;
        }, cancellationToken: new(kind == "canceled")));
        if (kind is "throw" or "validation") await Assert.That(error).IsSameReferenceAs(expected);
        else if (kind == "null-task") await Assert.That(error).IsTypeOf<InvalidOperationException>();
        else await Assert.That(error).IsTypeOf<OperationCanceledException>();
        await Assert.That(calls).IsEqualTo(kind is "canceled" or "validation" ? 0 : 1);
        await Assert.That(completion.Calls.Contains("commit")).IsFalse();
        await Assert.That(completion.Calls.TakeLast(2).SequenceEqual(["dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(0);
    }

    [Test]
    public async Task PublicLifecycle_UnsupportedProviderDoesNotCreateSynchronousTransactions()
    {
        using var fixture = new ScriptedFixture();
        await Assert.That(await AsyncEnumerationFailureOf(() => fixture.Database.CommitAsync(_ => Task.CompletedTask))).IsTypeOf<NotSupportedException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => fixture.Provider.CommitAsync(_ => Task.FromResult(1)))).IsTypeOf<NotSupportedException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => fixture.Database.InsertAsync(new Mutable<TransactionMutationGuardRow>()))).IsTypeOf<NotSupportedException>();
        await Assert.That(fixture.Scenario.CreatedTransactionTypes).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicLifecycle_CapturedOptionsBoundAutomaticRollback(bool callback)
    {
        var scenario = new ScriptedMutationScenario { AsyncCompletion = new() { Rollback = new(paused: true) } };
        var options = new DataLinqExecutionOptions { RecoveryRollbackTimeout = TimeSpan.FromMilliseconds(20) };
        using var provider = new ScriptedMutationProvider<TransactionMutationGuardDb>(scenario, executionOptions: options);
        var database = new ScriptedDatabase(provider);
        var expected = new Exception("application");
        Exception error;
        if (callback)
            error = await AsyncEnumerationFailureOf(() => database.CommitAsync<int>(_ => throw expected));
        else
        {
            var transaction = database.Transaction();
            error = await AsyncEnumerationFailureOf(() => transaction.DisposeAsync().AsTask());
            await Assert.That(transaction.IsDisposed).IsTrue();
        }
        if (callback) await Assert.That(error).IsSameReferenceAs(expected);
        else await Assert.That(error).IsTypeOf<OperationCanceledException>();
        await Assert.That(scenario.AsyncCompletion.RollbackToken.IsCancellationRequested).IsTrue();
        await Assert.That(scenario.AsyncCompletion.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(ReferenceEquals(provider.ExecutionOptions, options)).IsFalse();
        await Assert.That(((IDatabaseProvider)provider).ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(options.RecoveryRollbackTimeout);
    }

    [Test]
    public async Task PublicLifecycle_SourceLessDeleteCannotBypassPoisonedOrigin()
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        using var origin = fixture.Database.Transaction();
        var row = fixture.CreateImmutable(81, "origin", origin);
        var expected = new Exception("write failed");
        fixture.Scenario.EnqueueNonQueryFailure(expected);
        await Assert.That(Capture<Exception>(() => origin.Delete(row))).IsSameReferenceAs(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => row.DeleteAsync())).IsTypeOf<TransactionPoisonedException>();
        await Assert.That(fixture.Scenario.CreatedTransactionTypes.Count).IsEqualTo(1);
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls).IsEmpty();
    }
}
