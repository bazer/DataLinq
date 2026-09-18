using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawCommands_ShareManagedAdmissionAndPreserveOwnership(bool scalar, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand { CommandText = "UPDATE rows RETURNING value", CommandTimeout = 23 };
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 7, NonQueryResult = 7 };
        var factory = new ControlledEagerCommandFactory { Access = access,
            ConfigureCommand = owned => owned.Resource.Cleanup = new(paused: true) };
        fixture.Scenario.AsyncCommands = factory;
        fixture.PrimeCommittedRow(1, "old");
        var pending = ExecuteRaw(transaction.DatabaseAccess, scalar, borrowed ? command : null);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => transaction.Delete(fixture.CreateImmutable(2, "pending")));
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Rollback);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            var overlap = await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(command));
            await Assert.That(overlap).IsTypeOf<InvalidOperationException>();
            await Assert.That(access.Calls.Count(x => x.StartsWith("dispatch", StringComparison.Ordinal))).IsEqualTo(1);
            if (borrowed) await Assert.That(access.ObservedCommand).IsSameReferenceAs(command);
        }
        finally { access.Dispatch.Release(); }
        if (!borrowed)
        {
            var cleanup = factory.Commands[0].Resource.Cleanup;
            await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await Assert.That(pending.IsCompleted).IsFalse();
                _ = Capture<InvalidOperationException>(() => transaction.Query());
            }
            finally { cleanup.Release(); }
        }
        await Assert.That(await pending).IsEqualTo(7);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.CommandText).IsEqualTo("UPDATE rows RETURNING value");
        await Assert.That(command.CommandTimeout).IsEqualTo(23);
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        if (!borrowed) await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        transaction.Commit();
        await Assert.That(fixture.Database.Get<TransactionMutationGuardRow, int>(1)!.Value).IsEqualTo("old");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRawCommands_ValidateCapabilityBeforeCancellationWithoutInitialization(bool scalar, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var lazy = BindInitialization(fixture, transaction, resource);
        var expected = new NotSupportedException("unsupported command");
        var factory = new ControlledEagerCommandFactory { Initialization = lazy,
            Access = new() { ValidationFailure = expected },
            ConfigureCommand = command => command.ValidationFailure = expected };
        fixture.Scenario.AsyncCommands = factory;
        using var command = new ControlledCommand();
        var failure = await AsyncEnumerationFailureOf(() => ExecuteRaw(transaction.DatabaseAccess, scalar, borrowed ? command : null, new(true)));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_PreCancellationDoesNotReserveOrCreate(bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledEagerCommandFactory();
        fixture.Scenario.AsyncCommands = factory;
        using var command = new ControlledCommand();
        await Assert.That(await AsyncEnumerationFailureOf(() => ExecuteRaw(transaction.DatabaseAccess, false, borrowed ? command : null, new(true)))).IsTypeOf<OperationCanceledException>();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await Assert.That(factory.Access.Calls.Any(x => x.StartsWith("dispatch", StringComparison.Ordinal))).IsFalse();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task AsyncRawCommands_PostDispatchFailureNeverAcquiresOrdinaryReadReuse(bool scalar, bool borrowed, bool cancel)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        using var command = new ControlledCommand();
        var expected = new Exception("dispatch");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var factory = new ControlledEagerCommandFactory { Access = access };
        fixture.Scenario.AsyncCommands = factory;
        var pending = ExecuteRaw(transaction.DatabaseAccess, scalar, borrowed ? command : null, cancellation.Token);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancel) cancellation.Cancel(); else access.Dispatch.Fail(expected);
        var failure = await AsyncEnumerationFailureOf(() => pending);
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(transaction.Commit);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        if (!borrowed) await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        transaction.Rollback();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_ConstructionOrPostInitializationCancellationPreservesPriorWork(bool cancel)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Initialized = () => { if (cancel) cancellation.Cancel(); } };
        var lazy = BindInitialization(fixture, transaction, resource);
        var expected = new Exception("create");
        var factory = new ControlledEagerCommandFactory { Initialization = lazy,
            ConfigureCommand = command => command.Creating = () => throw expected };
        fixture.Scenario.AsyncCommands = factory;
        var failure = await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows", cancellation.Token));
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(cancel ? 0 : 1);
        _ = transaction.Query();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments("open", false)]
    [Arguments("configure", false)]
    [Arguments("begin", false)]
    [Arguments("open", true)]
    [Arguments("configure", true)]
    [Arguments("begin", true)]
    public async Task AsyncRawCommands_InterruptedInitializationIsPrivateTerminalAndDrained(string phase, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Cleanup = new(paused: true) };
        var pause = PauseInitialization(resource, phase);
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledEagerCommandFactory { Initialization = lazy };
        fixture.Scenario.AsyncCommands = factory;
        using var command = new ControlledCommand();
        var pending = ExecuteRaw(transaction.DatabaseAccess, false, borrowed ? command : null);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("initialize");
        pause.Fail(expected);
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(lazy.PublishedResource).IsNull();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { resource.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(command, new(true)))).IsTypeOf<InvalidOperationException>();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_CleanupAndAssessmentFailuresPreservePrimaryAndOrder(bool sameCleanup)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("dispatch");
        var cleanup = sameCleanup ? expected : new Exception("cleanup");
        var assessment = new Exception("assessment");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { EvidenceFailure = assessment };
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Access = access,
            ConfigureCommand = command => command.Resource.Disposing = () => throw cleanup };
        var pending = transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows");
        access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(sameCleanup ? 1 : 2);
        await Assert.That(context.SecondaryFailures.Last().Exception).IsSameReferenceAs(assessment);
        if (!sameCleanup) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
    }

    [Test]
    public async Task AsyncRawCommands_ScalarConversionIsCapturedAfterCleanupAndUnderOwnership()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 7 };
        var factory = new ControlledEagerCommandFactory { Access = access,
            ConfigureCommand = command => command.Resource.Disposing = cancellation.Cancel };
        factory.ScalarConverting = value =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            if (factory.Commands[0].Resource.AsyncDisposals != 1) throw new Exception("conversion before cleanup");
            return (int)value! + 1;
        };
        fixture.Scenario.AsyncCommands = factory;
        var pending = transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>("UPDATE rows RETURNING value", cancellation.Token);
        factory.ScalarConverting = _ => throw new Exception("later policy");
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory();
        access.Dispatch.Release();
        await Assert.That(await pending).IsEqualTo(8);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
    }

    [Test]
    public async Task AsyncRawCommands_ScalarConversionFailureAfterDispatchIsConservative()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new FormatException("convert");
        var factory = new ControlledEagerCommandFactory { Access = new() { FailureEvidence = TrustedScalarRead },
            ScalarConverting = _ => throw expected };
        fixture.Scenario.AsyncCommands = factory;
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>("SELECT value"))).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(transaction.AsyncFailureContext.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncRawCommands_StandaloneNullPoliciesAndFailureHaveNoTransactionRecovery()
    {
        using var fixture = new ScriptedFixture();
        var factory = new ControlledEagerCommandFactory { Access = new() { ScalarResult = null }, ScalarConverting = value => value ?? 17 };
        fixture.Scenario.AsyncCommands = factory;
        var access = fixture.Provider.ReadOnlyAccess.DatabaseAccess;
        using var command = new ControlledCommand();
        await Assert.That(await access.ExecuteScalarAsyncCore<int>(command)).IsEqualTo(17);
        factory.Access.ScalarResult = DBNull.Value;
        await Assert.That(await access.ExecuteScalarAsyncCore("SELECT NULL")).IsSameReferenceAs(DBNull.Value);
        await Assert.That(await access.ExecuteScalarAsyncCore(command)).IsSameReferenceAs(DBNull.Value);
        factory.Access = new(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var expected = new Exception("standalone");
        var pending = access.ExecuteNonQueryAsyncCore("UPDATE rows");
        factory.Access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_PrivateOwnerDoesNotReacquireAndRejectsWrongTransaction(bool scalar)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Access = new() { NonQueryResult = 7, ScalarResult = 7 } };
        using var command = new ControlledCommand();
        using (var scope = DataSourceAccess.BeginRead(transaction, "managed operation"))
        {
            if (scalar) await Assert.That(await transaction.DatabaseAccess.ExecuteScalarOwnedAsyncCore(command, scope!.Step, default)).IsEqualTo((object)7);
            else await Assert.That(await transaction.DatabaseAccess.ExecuteNonQueryOwnedAsyncCore(command, scope!.Step, default)).IsEqualTo(7);
            await Assert.That(await AsyncEnumerationFailureOf(() => ExecuteRaw(transaction.DatabaseAccess, scalar, command))).IsTypeOf<InvalidOperationException>();
            await Assert.That(await AsyncEnumerationFailureOf(() => other.DatabaseAccess.ExecuteNonQueryOwnedAsyncCore(command, scope!.Step, default))).IsTypeOf<InvalidOperationException>();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_HelperDrainsUnfinishedExecutionAndCleanupWithoutCommitting(bool fail)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 7, FailureEvidence = TrustedScalarRead };
        var factory = new ControlledEagerCommandFactory { Access = access,
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true) };
        fixture.Scenario.AsyncCommands = factory;
        Task<int>? commandTask = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            commandTask = transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows");
            return Task.FromResult(9);
        }, new());
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("unfinished command");
        if (fail) access.Dispatch.Fail(expected); else access.Dispatch.Release();
        var cleanup = factory.Commands[0].Resource.Cleanup;
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { await Assert.That(helper.IsCompleted).IsFalse(); }
        finally { cleanup.Release(); }
        if (fail) await Assert.That(await AsyncEnumerationFailureOf(() => commandTask!)).IsSameReferenceAs(expected);
        else await Assert.That(await commandTask!).IsEqualTo(7);
        var failure = await AsyncEnumerationFailureOf(() => helper);
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        if (fail) await Assert.That(ExecutionFailureContexts.Get(failure)!.SecondaryFailures.Any(x => ReferenceEquals(x.Exception, expected))).IsTrue();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_UnverifiedDbCommandHasNoSynchronousFallback(bool scalar)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledEagerCommandFactory();
        fixture.Scenario.AsyncCommands = factory;
        using var command = new UnverifiedCommand();
        await Assert.That(await AsyncEnumerationFailureOf(() => ExecuteRaw(transaction.DatabaseAccess, scalar, command, new(true)))).IsTypeOf<NotSupportedException>();
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_ActualOwnedCommandValidationCleansUpBeforeRecovery(bool cleanupFails)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new NotSupportedException("actual command");
        var cleanup = new Exception("cleanup");
        var factory = new ControlledEagerCommandFactory { Access = new() { ValidationFailure = expected },
            ConfigureCommand = command => command.Resource.Disposing = () => { if (cleanupFails) throw cleanup; } };
        fixture.Scenario.AsyncCommands = factory;
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows"))).IsSameReferenceAs(expected);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Access.Calls.Any(x => x.StartsWith("dispatch", StringComparison.Ordinal))).IsFalse();
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(!cleanupFails);
        if (cleanupFails) await Assert.That(transaction.AsyncFailureContext.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        else _ = transaction.Query();
    }

    [Test]
    public async Task AsyncRawCommands_InitializeOnceAndKeepCapturedResourcesThroughDispatch()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var pause = PauseInitialization(resource, "open");
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledEagerCommandFactory { Initialization = lazy,
            Access = new() { NonQueryResult = 7, ScalarResult = 8 } };
        fixture.Scenario.AsyncCommands = factory;
        var pending = transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows");
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var selected = factory.Access;
        try
        {
            factory.Access = new() { NonQueryResult = 19, ScalarResult = 21 };
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { pause.Release(); }
        await Assert.That(await pending).IsEqualTo(7);
        await Assert.That(selected.Calls.Contains("dispatch:NonQuery")).IsTrue();
        using var command = new ControlledCommand();
        await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>(command)).IsEqualTo(21);
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncRawCommands_RetiredOwnerAndTerminalTransactionCannotDispatch()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledEagerCommandFactory();
        fixture.Scenario.AsyncCommands = factory;
        using var command = new ControlledCommand();
        var scope = DataSourceAccess.BeginRead(transaction, "managed operation")!;
        var retired = scope.Step;
        scope.Dispose();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryOwnedAsyncCore(command, retired, default))).IsTypeOf<InvalidOperationException>();
        transaction.Commit();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(command, new(true)))).IsTypeOf<InvalidOperationException>();
        await Assert.That(factory.Access.Calls.Any(x => x.StartsWith("dispatch", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRawCommands_PreDispatchRejectionPreservesTrackedWork(bool unsupported)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(fixture.CreateImmutable(123, "delete"));
        var factory = new ControlledEagerCommandFactory
        {
            ConfigureCommand = command => { if (unsupported) command.ValidationFailure = new NotSupportedException("capability"); }
        };
        fixture.Scenario.AsyncCommands = factory;
        var failure = await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows", new(true)));
        await Assert.That(unsupported ? failure is NotSupportedException : failure is OperationCanceledException).IsTrue();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        transaction.Commit();
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncRawCommands_PostDispatchNoStatementClaimCannotRestoreContinue()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
        {
            FailureEvidence = new(Effects: ExecutionEffects.NoStatement, Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true)
        };
        fixture.Scenario.AsyncCommands = new ControlledEagerCommandFactory { Access = access };
        var pending = transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT value");
        access.Dispatch.Fail(new Exception("dispatch"));
        _ = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
    }

    [Test]
    public async Task AsyncRawCommands_CleanupFailurePreventsSuccessfulResult()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("cleanup after success");
        var factory = new ControlledEagerCommandFactory
        {
            Access = new() { NonQueryResult = 7, FailureEvidence = TrustedScalarRead },
            ConfigureCommand = command => command.Resource.Disposing = () => throw expected
        };
        fixture.Scenario.AsyncCommands = factory;
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE rows"))).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    private static async Task<int> ExecuteRaw(DatabaseAccess access, bool scalar, IDbCommand? command, CancellationToken token = default)
    {
        if (scalar) return command is null ? await access.ExecuteScalarAsyncCore<int>("UPDATE rows RETURNING value", token)
            : await access.ExecuteScalarAsyncCore<int>(command, token);
        return command is null ? await access.ExecuteNonQueryAsyncCore("UPDATE rows", token)
            : await access.ExecuteNonQueryAsyncCore(command, token);
    }
}
