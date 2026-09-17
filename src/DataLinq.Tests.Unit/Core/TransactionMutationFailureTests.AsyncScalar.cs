using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task AsyncScalar_CapturesQueryFactoryAndConversion_AndRetainsAdmissionThroughCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 42 };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true),
            ScalarConverting = value => { _ = Capture<InvalidOperationException>(() => transaction.Query()); return (int)value! + 1; }
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var bytes = new byte[] { 1 };
        var query = transaction.From<TransactionMutationGuardBinaryRow>();
        query.Where("id").EqualTo(bytes);
        var select = query.SelectQuery().What("COUNT(*)");
        var pending = select.ExecuteScalarAsyncCore<int>();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var sql = factory.Inputs[0].Text;
        select.What("id");
        bytes[0] = 9;
        factory.ScalarConverting = _ => throw new Exception("later conversion");
        var replacement = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlScalars = replacement;
        access.Dispatch.Release();
        var cleanup = factory.Commands[0].Resource.Cleanup;
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(access.ObservedCommand!.CommandText).IsEqualTo(sql);
            await Assert.That(((byte[])factory.Inputs[0].ToSql().Parameters[0].Value!)[0]).IsEqualTo((byte)1);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        finally { cleanup.Release(); await pending; }
        await Assert.That(await pending).IsEqualTo(43);
        await Assert.That(replacement.Inputs).IsEmpty();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncScalar_PreCancellationAndUnsupportedCapabilityDoNotInitializeOrPoison(bool unsupported)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledSqlReaderFactory
        {
            WrapScalar = source => new InitializingTransactionScalarSource<ControlledTransactionResource>(lazy, source),
            ConfigureCommand = command => { if (unsupported) command.ValidationFailure = new NotSupportedException("scalar"); }
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var error = await AsyncEnumerationFailureOf(() => transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore(new(true)));
        await Assert.That(unsupported ? error is NotSupportedException : error is OperationCanceledException).IsTrue();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(factory.Commands[0].Validations.All(x => x == AsyncCommandKind.Scalar)).IsTrue();
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
    public async Task AsyncScalar_InterruptedInitializationStaysPrivateAndTerminal(string phase, bool cancel)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Cleanup = new(paused: true) };
        var pause = PauseInitialization(resource, phase);
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledSqlReaderFactory
        {
            WrapScalar = source => new InitializingTransactionScalarSource<ControlledTransactionResource>(lazy, source),
            CreateAccess = _ => new() { FailureEvidence = TrustedScalarRead }
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var pending = select.ExecuteScalarAsyncCore(cancellation.Token);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("initialization");
        if (cancel) cancellation.Cancel(); else pause.Fail(expected);
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { resource.Cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
        await Assert.That(await AsyncEnumerationFailureOf(() => select.ExecuteScalarAsyncCore(new(true)))).IsTypeOf<InvalidOperationException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsTypeOf<InvalidOperationException>();
        await transaction.DisposeAsyncCore();
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncScalar_SuccessfulInitializationThenCancellationLeavesNoCommandAndCanContinue()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Initialized = cancellation.Cancel };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledSqlReaderFactory
        {
            WrapScalar = source => new InitializingTransactionScalarSource<ControlledTransactionResource>(lazy, source),
            CreateAccess = _ => new() { ScalarResult = 7 }
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        await Assert.That(await AsyncEnumerationFailureOf(() => select.ExecuteScalarAsyncCore(cancellation.Token))).IsTypeOf<OperationCanceledException>();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(await select.ExecuteScalarAsyncCore<int>()).IsEqualTo(7);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncScalar_RecoveryRequiresReadTrustAndSuccessfulCleanup(bool trusted, bool cleanupFails)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("dispatch");
        var cleanup = new Exception("cleanup");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = trusted ? TrustedScalarRead : new() };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = command => { if (cleanupFails) command.Resource.Disposing = () => throw cleanup; }
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var pending = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(trusted && !cleanupFails);
        await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanupFails);
        if (cleanupFails) await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncScalar_SameExecutionAndCleanupExceptionCannotRestoreContinue()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("both");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = command => command.Resource.Disposing = () => throw expected
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var pending = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore();
        access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.AfterRecovery(ExecutionCompletion.RolledBack, ExecutionRecoveryActions.Dispose).HasCleanupFailure).IsTrue();
    }

    [Test]
    public async Task AsyncScalar_ConversionFailureIsMaterializationAfterCleanup_AndLateCancellationDoesNotUndoSuccess()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ScalarResult = 7, FailureEvidence = TrustedScalarRead },
            ConfigureCommand = command => command.Resource.Disposing = cancellation.Cancel
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        await Assert.That(await select.ExecuteScalarAsyncCore<int>(cancellation.Token)).IsEqualTo(7);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        var failure = await AsyncEnumerationFailureOf(() => select.ExecuteScalarAsyncCore<Guid>());
        await Assert.That(failure).IsTypeOf<InvalidCastException>();
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(transaction.AsyncFailureContext.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
    }

    [Test]
    public async Task AsyncScalar_RetainsProviderNullPolicyAndRawDbNullWithoutGenericCoercion()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ScalarResult = null }, ScalarConverting = value => value ?? 0 };
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = fixture.Database.From<TransactionMutationGuardRow>().SelectQuery();
        await Assert.That(await select.ExecuteScalarAsyncCore<int>()).IsEqualTo(0);
        factory.CreateAccess = _ => new() { ScalarResult = DBNull.Value };
        await Assert.That(await select.ExecuteScalarAsyncCore()).IsSameReferenceAs(DBNull.Value);
    }

    [Test]
    public async Task AsyncScalar_HelperAwaitsUnfinishedWorkAndCleanup_ThenRejectsCallbackResult()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 7 };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlScalars = factory;
        Task<object?>? scalar = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            scalar = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore();
            return Task.FromResult(9);
        }, new());
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(helper.IsCompleted).IsFalse();
        access.Dispatch.Release();
        await Assert.That(await scalar!).IsEqualTo(7);
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncScalar_FailedEvidenceAssessmentKeepsOriginalFailureAndPreventsReuse()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("execution");
        var assessment = new Exception("assessment");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
        {
            FailureEvidence = TrustedScalarRead,
            EvidenceFailure = assessment,
            AssessingFailure = () => { _ = Capture<InvalidOperationException>(() => transaction.Query()); }
        };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlScalars = factory;
        var pending = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore();
        access.Dispatch.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.AsyncFailureContext.SecondaryFailures.Single().Exception).IsSameReferenceAs(assessment);
        await Assert.That(transaction.AsyncFailureContext.SecondaryFailures.Single().Stage).IsEqualTo(ExecutionFailureStage.Recovery);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    private static ReadFailureEvidence TrustedScalarRead => new(Effects: ExecutionEffects.OrdinaryRead, Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true);
}
