using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.SQLite;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(SQLiteJournalMode.OFF)]
    [Arguments(SQLiteJournalMode.DELETE)]
    [Arguments(SQLiteJournalMode.TRUNCATE)]
    [Arguments(SQLiteJournalMode.PERSIST)]
    [Arguments(SQLiteJournalMode.MEMORY)]
    [Arguments(SQLiteJournalMode.WAL)]
    public async Task JournalMode_CapturesProviderModeAndExecutesOneOwnedNonQuery(SQLiteJournalMode mode)
    {
        var harness = new JournalModeHarness();
        using var provider = new JournalModeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        await Assert.That(provider.Captures).IsEqualTo(0);
        await Assert.That(harness.Session.Opens).IsEqualTo(0);
        await provider.SetJournalModeAsyncCore(mode, cancellation.Token);
        await Assert.That(harness.CapturedMode).IsEqualTo(mode);
        await Assert.That(harness.Commands.Resource.Borrowed.CommandText).IsEqualTo("PRAGMA journal_mode = " + mode);
        await Assert.That(harness.Access.Calls.Count(x => x == "dispatch:NonQuery")).IsEqualTo(1);
        await Assert.That(harness.Access.Calls.Any(x => x is "dispatch:Scalar" or "dispatch:Reader")).IsFalse();
        await Assert.That(harness.Session.Opening.ObservedToken).IsEqualTo(cancellation.Token);
        await Assert.That(harness.Access.Dispatch.ObservedToken).IsEqualTo(cancellation.Token);
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(harness.Commands.Resource.SyncDisposals).IsEqualTo(0);
        await Assert.That(harness.Commands.Resource.Borrowed.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(provider.SyncCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(7)]
    public async Task JournalMode_NonQueryResultDoesNotBecomeAnEffectiveModeOrRowCountContract(int result)
    {
        var harness = new JournalModeHarness();
        harness.Access.NonQueryResult = result;
        using var provider = new JournalModeProvider(new(), () => harness);
        await provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL);
        await Assert.That(harness.Commands.Creates).IsEqualTo(1);
        await Assert.That(harness.Access.Calls.Count(x => x.StartsWith("dispatch:", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalMode_UnsupportedProviderRejectsBeforeCancellationWithoutSyncFallback(bool canceled)
    {
        using var provider = new LegacyJournalModeProvider(new());
        var failure = await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, new(canceled)));
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(provider.SyncCalls).IsEqualTo(0);
    }

    [Test]
    public async Task JournalMode_ModeTypeCannotAccidentallyBindByItsNumericValue()
    {
        var harness = new JournalModeHarness();
        using var provider = new JournalModeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(OtherJournalMode.Wal, new(true)));
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(provider.Captures).IsEqualTo(0);
        await Assert.That(harness.Session.Opens).IsEqualTo(0);
    }

    [Test]
    public async Task JournalMode_NullProviderPrecedesCancellation()
    {
        var failure = await AsyncEnumerationFailureOf(() => AsyncJournalMode.SetJournalModeAsyncCore((IDatabaseProvider)null!, SQLiteJournalMode.WAL, new(true)));
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalMode_ValidationPrecedesPreCancellationWithoutOpening(bool invalid)
    {
        var harness = new JournalModeHarness();
        var expected = new ObjectDisposedException("journal-mode source");
        if (invalid) harness.ValidationFailure = expected;
        using var provider = new JournalModeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, new(true)));
        if (invalid) await Assert.That(failure).IsSameReferenceAs(expected);
        else await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(harness.Validations).IsEqualTo(1);
        await Assert.That(harness.Creates).IsEqualTo(0);
        await Assert.That(harness.Commands.Creates).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(0);
    }

    [Test]
    public async Task JournalMode_CapturesIdentityAndSessionCollaboratorsBeforeOpeningSuspends()
    {
        var harness = new JournalModeHarness();
        var replacement = new JournalModeHarness();
        harness.Session.Opening = new(paused: true);
        using var provider = new JournalModeProvider(new(), () => harness);
        provider.SetEffectiveName("existing-named-memory");
        var mode = SQLiteJournalMode.MEMORY;
        var pending = provider.SetJournalModeAsyncCore(mode);
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        mode = SQLiteJournalMode.DELETE;
        provider.SetEffectiveName("replacement-identity");
        harness.Session.Access = replacement.Access;
        harness.Session.CommandFactory = replacement.Commands;
        harness.Session.Opening.Release();
        await pending;
        await Assert.That(harness.CapturedMode).IsEqualTo(SQLiteJournalMode.MEMORY);
        await Assert.That(harness.Identity).IsEqualTo("existing-named-memory");
        await Assert.That(harness.Commands.Resource.Borrowed.CommandText).IsEqualTo("PRAGMA journal_mode = MEMORY");
        await Assert.That(replacement.Commands.Creates).IsEqualTo(0);
        await Assert.That(harness.Session.AccessReads).IsEqualTo(1);
        await Assert.That(harness.Session.FactoryReads).IsEqualTo(1);
        await Assert.That(mode).IsEqualTo(SQLiteJournalMode.DELETE);
    }

    [Test]
    public async Task JournalMode_ExplicitFreshSessionsDoNotBorrowApplicationTransactions()
    {
        var first = new JournalModeHarness();
        var second = new JournalModeHarness();
        var queue = new Queue<JournalModeHarness>([first, second]);
        var scenario = new ScriptedMutationScenario();
        using var provider = new JournalModeProvider(scenario, queue.Dequeue);
        using var transaction = provider.StartTransaction();
        await Assert.That(provider.Captures).IsEqualTo(0);
        await provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL);
        await provider.SetJournalModeAsyncCore(SQLiteJournalMode.DELETE);
        await Assert.That(provider.Captures).IsEqualTo(2);
        await Assert.That(first.Session.Disposals).IsEqualTo(1);
        await Assert.That(second.Session.Disposals).IsEqualTo(1);
        await Assert.That(first.CapturedMode).IsEqualTo(SQLiteJournalMode.WAL);
        await Assert.That(second.CapturedMode).IsEqualTo(SQLiteJournalMode.DELETE);
        await Assert.That(scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(scenario.Commits).IsEqualTo(0);
        await Assert.That(scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(scenario.Disposals).IsEqualTo(0);
    }

    [Test]
    [Arguments("create")]
    [Arguments("access")]
    [Arguments("factory")]
    [Arguments("factory-validation")]
    [Arguments("open")]
    [Arguments("command-create")]
    [Arguments("command-get")]
    [Arguments("command-validation")]
    [Arguments("execute")]
    [Arguments("command-cleanup")]
    [Arguments("session-cleanup")]
    public async Task JournalMode_OriginalFailuresEscapeAfterEveryOwnedResourceSettles(string phase)
    {
        var harness = new JournalModeHarness();
        var expected = new InvalidOperationException(phase);
        switch (phase)
        {
            case "create": harness.Creating = () => throw expected; break;
            case "access": harness.Session.AccessFailure = expected; break;
            case "factory": harness.Session.FactoryFailure = expected; break;
            case "factory-validation": harness.Commands.ValidationFailure = expected; break;
            case "open": harness.Session.Opening = JournalFault(expected); break;
            case "command-create": harness.Commands.Creating = () => throw expected; break;
            case "command-get": harness.Commands.Resource.CommandFailure = expected; break;
            case "command-validation": harness.Access.ValidationFailure = expected; break;
            case "execute": harness.Session.Access = new ControlledAsyncDatabaseAccess(JournalFault(expected)); break;
            case "command-cleanup": harness.Commands.Resource.Cleanup = JournalFault(expected); break;
            case "session-cleanup": harness.Session.Cleanup = JournalFault(expected); break;
        }
        using var provider = new JournalModeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL))).IsSameReferenceAs(expected);
        await Assert.That(harness.Creates).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(phase == "create" ? 0 : 1);
        await Assert.That(harness.Commands.Creates <= 1).IsTrue();
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(
            phase is "command-get" or "command-validation" or "execute" or "command-cleanup" or "session-cleanup" ? 1 : 0);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalMode_RequestCancellationReachesOpeningAndExecution(bool opening)
    {
        var harness = new JournalModeHarness();
        var pause = new AsyncCheckpoint(paused: true);
        if (opening) harness.Session.Opening = pause;
        else harness.Session.Access = new ControlledAsyncDatabaseAccess(pause);
        using var provider = new JournalModeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, cancellation.Token);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(pause.ObservedToken).IsEqualTo(cancellation.Token);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(harness.Session.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        await Assert.That(harness.Commands.Creates).IsEqualTo(opening ? 0 : 1);
    }

    [Test]
    public async Task JournalMode_NonCooperativeOpeningIsDrainedBeforeCancellationEscapes()
    {
        var harness = new JournalModeHarness();
        harness.Session.Opening = new(paused: true);
        harness.Session.IgnoreCancellation = true;
        using var provider = new JournalModeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, cancellation.Token);
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(harness.Session.Disposals).IsEqualTo(0);
            await Assert.That(harness.Session.OpenToken).IsEqualTo(cancellation.Token);
        }
        finally { harness.Session.Opening.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending) is OperationCanceledException).IsTrue();
        await Assert.That(harness.Commands.Creates).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalMode_NonCooperativeCommandMustSettleWithoutRetryOrAbandonment(bool fails)
    {
        var harness = new JournalModeHarness();
        var access = new UncooperativeJournalAccess();
        harness.Session.Access = access;
        using var provider = new JournalModeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, cancellation.Token);
        await access.Execution.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var expected = new InvalidOperationException("late execution failure");
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(0);
            await Assert.That(harness.Session.Disposals).IsEqualTo(0);
            await Assert.That(access.Token).IsEqualTo(cancellation.Token);
        }
        finally { if (fails) access.Execution.Fail(expected); else access.Execution.Release(); }
        if (fails) await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        else await pending;
        await Assert.That(access.Executions).IsEqualTo(1);
        await Assert.That(harness.Commands.Creates).IsEqualTo(1);
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task JournalMode_ConfirmedCommandIsNotRetroactivelyCanceledButCleanupMustSucceed(bool sessionCleanup, bool cleanupFails)
    {
        var harness = new JournalModeHarness();
        var cleanup = new AsyncCheckpoint(paused: true);
        if (sessionCleanup) harness.Session.Cleanup = cleanup;
        else harness.Commands.Resource.Cleanup = cleanup;
        using var provider = new JournalModeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, cancellation.Token);
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var expected = new InvalidOperationException("cleanup after confirmed journal command");
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally { if (cleanupFails) cleanup.Fail(expected); else cleanup.Release(); }
        if (cleanupFails) await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        else await pending;
        await Assert.That(harness.Access.Calls.Count(x => x == "dispatch:NonQuery")).IsEqualTo(1);
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalMode_ExecutionAndCleanupFailuresRetainEncounterOrderAndIdentity(bool sameException)
    {
        var harness = new JournalModeHarness();
        var primary = new InvalidOperationException("journal command failed");
        var command = sameException ? primary : new InvalidOperationException("command cleanup failed");
        var session = sameException ? primary : new InvalidOperationException("session cleanup failed");
        harness.Session.Access = new ControlledAsyncDatabaseAccess(JournalFault(primary));
        harness.Commands.Resource.Cleanup = JournalFault(command);
        harness.Session.Cleanup = JournalFault(session);
        using var provider = new JournalModeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL))).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Select(x => x.Exception).SequenceEqual(sameException ? [] : new Exception[] { command, session })).IsTrue();
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalMode_MissingPlanOrSessionCannotSucceed(bool plan)
    {
        var harness = new JournalModeHarness();
        if (!plan) harness.Creating = () => null!;
        using var provider = new JournalModeProvider(new(), () => harness) { ReturnNoPlan = plan };
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL))).IsTypeOf<InvalidOperationException>();
        await Assert.That(harness.Session.Opens).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(0);
    }

    private static AsyncCheckpoint JournalFault(Exception failure)
    { var checkpoint = new AsyncCheckpoint(paused: true); checkpoint.Fail(failure); return checkpoint; }

    private enum OtherJournalMode { Wal = 5 }

    private class LegacyJournalModeProvider(ScriptedMutationScenario scenario) : ScriptedMutationProvider<TransactionMutationGuardDb>(scenario)
    {
        internal int SyncCalls { get; private set; }
        public void SetJournalMode(SQLiteJournalMode mode) => SyncCalls++;
    }

    private sealed class JournalModeProvider(ScriptedMutationScenario scenario, Func<JournalModeHarness> create)
        : LegacyJournalModeProvider(scenario), IAsyncJournalModeSource<SQLiteJournalMode>
    {
        internal int Captures { get; private set; }
        internal bool ReturnNoPlan { get; set; }
        internal void SetEffectiveName(string name) => DatabaseName = name;
        public IAsyncJournalModePlan CaptureJournalMode(SQLiteJournalMode mode)
        {
            Captures++;
            return ReturnNoPlan ? null! : create().Capture(mode, DatabaseName);
        }
    }

    private sealed class JournalModeHarness : IAsyncJournalModePlan
    {
        internal JournalModeHarness() => Session = new() { Access = Access, CommandFactory = Commands };
        internal ControlledAsyncDatabaseAccess Access { get; } = new() { NonQueryResult = -1 };
        internal ControlledOwnedCommandFactory Commands { get; } = new();
        internal JournalModeSession Session { get; }
        internal SQLiteJournalMode CapturedMode { get; private set; }
        internal string Identity { get; private set; } = "";
        internal Func<IAsyncJournalModeSession>? Creating { get; set; }
        internal Exception? ValidationFailure { get; set; }
        internal int Validations { get; private set; }
        internal int Creates { get; private set; }
        internal IAsyncJournalModePlan Capture(SQLiteJournalMode mode, string identity)
        {
            CapturedMode = mode;
            Identity = identity;
            var sql = "PRAGMA journal_mode = " + mode;
            Commands.Creating ??= () => { Commands.Resource.Borrowed.CommandText = sql; return Commands.Resource; };
            return this;
        }
        public void Validate() { Validations++; if (ValidationFailure is not null) throw ValidationFailure; }
        public IAsyncJournalModeSession CreateSession() { Creates++; return Creating is { } create ? create() : Session; }
    }

    private sealed class JournalModeSession : IAsyncJournalModeSession
    {
        private IAsyncDatabaseAccess access = null!;
        private IAsyncOwnedCommandFactory commands = null!;
        public IAsyncDatabaseAccess Access { get { AccessReads++; return AccessFailure is { } failure ? throw failure : access; } set => access = value; }
        public IAsyncOwnedCommandFactory CommandFactory { get { FactoryReads++; return FactoryFailure is { } failure ? throw failure : commands; } set => commands = value; }
        internal Exception? AccessFailure { get; set; }
        internal Exception? FactoryFailure { get; set; }
        internal AsyncCheckpoint Opening { get; set; } = new();
        internal AsyncCheckpoint Cleanup { get; set; } = new();
        internal int Opens { get; private set; }
        internal int Disposals { get; private set; }
        internal int AccessReads { get; private set; }
        internal int FactoryReads { get; private set; }
        internal bool IgnoreCancellation { get; set; }
        internal CancellationToken OpenToken { get; private set; }
        public async Task OpenAsync(CancellationToken token)
        { Opens++; OpenToken = token; await Opening.ReachAsync(IgnoreCancellation ? CancellationToken.None : token); }
        public async ValueTask DisposeAsync() { Disposals++; await Cleanup.ReachAsync(CancellationToken.None); }
    }

    private sealed class UncooperativeJournalAccess : AsyncDatabaseAccess
    {
        internal AsyncCheckpoint Execution { get; } = new(paused: true);
        internal CancellationToken Token { get; private set; }
        internal int Executions { get; private set; }
        protected override void ValidateCommand(IDbCommand command, AsyncCommandKind kind)
        { if (command is not ControlledCommand || kind != AsyncCommandKind.NonQuery) throw new NotSupportedException(); }
        protected override async Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken token)
        { Executions++; Token = token; await Execution.ReachAsync(CancellationToken.None); return -1; }
        protected override Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken token) => throw new NotSupportedException();
        protected override Task<IAsyncDataReader> ExecuteReaderCoreAsync(IDbCommand command, CancellationToken token) => throw new NotSupportedException();
    }
}
