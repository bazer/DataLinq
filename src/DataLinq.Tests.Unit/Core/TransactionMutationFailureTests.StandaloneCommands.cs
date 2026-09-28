using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StandaloneReaders_HoldAdmissionThroughAcquisitionReaderCleanupAndCommandCleanup(bool sequence)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true));
        access.Reader.Cleanup = new(paused: true);
        var factory = RawFactory(() => access);
        factory.ConfigureCommand = command => command.Resource.Cleanup = new(paused: true);
        fixture.Scenario.AsyncSqlReaders = factory;
        var rows = sequence ? transaction.ReadReaderAsync("SELECT value").GetAsyncEnumerator() : null;
        Task<IDataLinqAsyncDataReader>? acquisition = sequence ? null : transaction.ExecuteReaderAsync("SELECT value");
        var opening = sequence ? rows!.MoveNextAsync().AsTask() : (Task)acquisition!;
        Task? closing = null;
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(() => transaction.BeginStandaloneCommand());
            access.Dispatch.Release();
            await opening;
            closing = sequence ? rows!.DisposeAsync().AsTask() : (await acquisition!).DisposeAsync().AsTask();
            await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(() => transaction.BeginStandaloneCommand());
            access.Reader.Cleanup.Release();
            await factory.Commands[0].Resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(closing.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.BeginStandaloneCommand());
        }
        finally
        {
            access.Dispatch.Release(); access.Reader.Cleanup.Release(); factory.Commands[0].Resource.Cleanup.Release();
            if (closing is not null) await closing;
            else if (sequence) await rows!.DisposeAsync();
            else await (await acquisition!).DisposeAsync();
        }
        using var available = transaction.BeginStandaloneCommand();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StandaloneCommands_HoldAdmissionThroughDispatchAndOwnedCommandCleanup(bool scalar)
    {
        using var fixture = new ScriptedFixture();
        var completion = fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 7, NonQueryResult = 7 };
        var factory = new ControlledEagerCommandFactory
        {
            Access = access, ConfigureCommand = command => command.Resource.Cleanup = new(paused: true)
        };
        fixture.Scenario.AsyncCommands = factory;
        var work = ExecuteRaw(transaction, scalar, null);
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(() => transaction.BeginStandaloneCommand());
            await Assert.That(await AsyncEnumerationFailureOf(() => Complete())).IsTypeOf<InvalidOperationException>();
            access.Dispatch.Release();
            await factory.Commands[0].Resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(work.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.BeginStandaloneCommand());
            await Assert.That(await AsyncEnumerationFailureOf(() => Complete())).IsTypeOf<InvalidOperationException>();
        }
        finally { access.Dispatch.Release(); factory.Commands[0].Resource.Cleanup.Release(); }
        await Assert.That(await work).IsEqualTo(7);
        await Complete();
        await Assert.That(completion.Calls.Contains("commit")).IsTrue();
        Task Complete() => transaction.CompleteStandaloneAsync((IAsyncTransactionCompletion)transaction, false, default);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StandaloneCommands_PreserveFailureAndDoNotGrantRollbackAfterCleanupFailure(bool cleanupFails)
    {
        using var fixture = new ScriptedFixture();
        var completion = fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        var expected = new FormatException("raw execution");
        var cleanup = new Exception("command cleanup");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var factory = new ControlledEagerCommandFactory
        {
            Access = access, ConfigureCommand = command => { if (cleanupFails) command.Resource.Disposing = () => throw cleanup; }
        };
        fixture.Scenario.AsyncCommands = factory;
        var work = transaction.ExecuteScalarAsync("SELECT value");
        access.Dispatch.Fail(expected);
        var actual = await AsyncEnumerationFailureOf(() => work);
        await Assert.That(actual).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(actual)!;
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Rollback)).IsEqualTo(!cleanupFails);
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
        _ = Capture<InvalidOperationException>(() => transaction.BeginStandaloneCommand());
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CompleteStandaloneAsync(
            (IAsyncTransactionCompletion)transaction, false, default))).IsTypeOf<InvalidOperationException>();
        await transaction.DisposeStandaloneAsync((IAsyncTransactionCompletion)transaction);
        await Assert.That(completion.Calls.Contains("rollback")).IsEqualTo(!cleanupFails);
        if (cleanupFails) await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }
}
