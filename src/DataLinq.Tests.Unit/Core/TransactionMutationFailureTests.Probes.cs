using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ExistenceProbe_InvalidTablePrecedesCaptureCancellationAndIO(string? table)
    {
        var harness = new ExistenceProbeHarness();
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.TableExistsAsyncCore(table!, token: new(true)));
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
        await Assert.That(provider.Captures).IsEqualTo(0);
        await Assert.That(harness.Session.Opens).IsEqualTo(0);
    }

    [Test]
    [Arguments("availability")]
    [Arguments("database")]
    [Arguments("table")]
    public async Task ExistenceProbe_LegacyProviderHasNoSynchronousFallback(string kind)
    {
        using var provider = new LegacyExistenceProbeProvider();
        var failure = await AsyncEnumerationFailureOf(() => RunProbe(provider, kind, new(true)));
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(provider.SyncCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("availability")]
    [Arguments("database")]
    [Arguments("table")]
    public async Task ExistenceProbe_NullProviderPrecedesCancellation(string kind)
    {
        await Assert.That(await AsyncEnumerationFailureOf(() => RunProbe(null!, kind, new(true)))).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    [Arguments("local", false)]
    [Arguments("scalar", false)]
    [Arguments("reader", false)]
    [Arguments("local", true)]
    [Arguments("scalar", true)]
    [Arguments("reader", true)]
    public async Task ExistenceProbe_ValidationPrecedesPreCancellationWithoutCreatingResources(string shape, bool invalid)
    {
        var harness = new ExistenceProbeHarness { Shape = shape };
        var expected = new NotSupportedException("unsupported probe capability");
        if (invalid) harness.ValidationFailure = expected;
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore(new(true)));
        if (invalid) await Assert.That(failure).IsSameReferenceAs(expected);
        else await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(harness.Validations).IsEqualTo(1);
        await Assert.That(harness.Creates).IsEqualTo(0);
        await Assert.That(harness.LocalReads).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(0);
    }

    [Test]
    [Arguments("local", false)]
    [Arguments("scalar", false)]
    [Arguments("reader", false)]
    [Arguments("local", true)]
    [Arguments("scalar", true)]
    [Arguments("reader", true)]
    public async Task ExistenceProbe_LocalScalarAndFirstRowShapesPreserveResultsAndOwnCleanup(string shape, bool exists)
    {
        var harness = new ExistenceProbeHarness(exists) { Shape = shape };
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        await Assert.That(await provider.DatabaseExistsAsyncCore(token: cancellation.Token)).IsEqualTo(exists);
        await Assert.That(harness.Session.Disposals).IsEqualTo(shape == "local" ? 0 : 1);
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(shape == "local" ? 0 : 1);
        await Assert.That(harness.Reader.AsyncDisposeCalls).IsEqualTo(shape == "reader" ? 1 : 0);
        await Assert.That(harness.Reader.AsyncReadCalls).IsEqualTo(shape == "reader" ? 1 : 0);
        await Assert.That(harness.Reader.SyncCalls).IsEqualTo(0);
        await Assert.That(harness.Commands.Resource.SyncDisposals).IsEqualTo(0);
        await Assert.That(harness.Commands.Resource.Borrowed.SyncExecutionCalls).IsEqualTo(0);
        if (shape != "local")
        {
            await Assert.That(harness.Session.Opening.ObservedToken).IsEqualTo(cancellation.Token);
            await Assert.That(harness.Access.Dispatch.ObservedToken).IsEqualTo(cancellation.Token);
            if (shape == "reader") await Assert.That(harness.Reader.Advance.ObservedToken).IsEqualTo(cancellation.Token);
        }
    }

    [Test]
    public async Task ExistenceProbe_LocalCheckExecutesDirectlyAndObservesCancellationAfterChecking()
    {
        var harness = new ExistenceProbeHarness { Shape = "local" };
        var caller = Environment.CurrentManagedThreadId;
        var observed = 0;
        harness.LocalRead = () => { observed = Environment.CurrentManagedThreadId; return true; };
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        var result = provider.FileOrServerExistsAsyncCore();
        await Assert.That(result.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await result).IsTrue();
        await Assert.That(observed).IsEqualTo(caller);
        using var cancellation = new CancellationTokenSource();
        harness.LocalRead = () => { cancellation.Cancel(); return false; };
        var failure = await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore(cancellation.Token));
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(harness.Creates).IsEqualTo(0);
    }

    [Test]
    public async Task ExistenceProbe_CapturesEffectiveIdentityArgumentsAndCollaboratorsBeforeOpening()
    {
        var harness = new ExistenceProbeHarness();
        var replacement = new ExistenceProbeHarness();
        harness.Session.Opening = new(paused: true);
        var originalInterpreterCalls = 0;
        harness.Interpret = value => { originalInterpreterCalls++; return value is not null; };
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        provider.SetEffectiveName("normalized-existing-memory");
        var pending = provider.TableExistsAsyncCore("ExactTable");
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        provider.SetEffectiveName("different-database");
        harness.Session.Access = replacement.Access;
        harness.Session.CommandFactory = replacement.Commands;
        harness.Interpret = _ => throw new InvalidOperationException("replacement interpreter");
        harness.Shape = "local";
        harness.Session.Opening.Release();
        await Assert.That(await pending).IsTrue();
        await Assert.That(harness.Commands.Resource.Borrowed.CommandText).IsEqualTo("normalized-existing-memory/ExactTable");
        await Assert.That(harness.Request!.Kind).IsEqualTo(ExistenceProbeKind.Table);
        await Assert.That(harness.Request.DatabaseName).IsNull();
        await Assert.That(harness.Request.TableName).IsEqualTo("ExactTable");
        await Assert.That(originalInterpreterCalls).IsEqualTo(1);
        await Assert.That(replacement.Commands.Creates).IsEqualTo(0);
    }

    [Test]
    public async Task ExistenceProbe_EachCallReadsFreshStateWithoutBorrowingAnApplicationTransaction()
    {
        var first = new ExistenceProbeHarness(false);
        var second = new ExistenceProbeHarness(true);
        var queue = new Queue<ExistenceProbeHarness>([first, second]);
        var scenario = new ScriptedMutationScenario();
        using var provider = new ExistenceProbeProvider(scenario, queue.Dequeue);
        using var transaction = provider.StartTransaction();
        await Assert.That(await provider.DatabaseExistsAsyncCore("requested-db")).IsFalse();
        await Assert.That(await provider.DatabaseExistsAsyncCore("requested-db")).IsTrue();
        await Assert.That(provider.Captures).IsEqualTo(2);
        await Assert.That(first.Commands.Resource.Borrowed.CommandText).IsEqualTo("requested-db/");
        await Assert.That(first.Session.Disposals).IsEqualTo(1);
        await Assert.That(second.Session.Disposals).IsEqualTo(1);
        await Assert.That(scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(scenario.Commits).IsEqualTo(0);
        await Assert.That(scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(scenario.Disposals).IsEqualTo(0);
    }

    [Test]
    [Arguments("availability", "open", true)]
    [Arguments("availability", "dispatch", true)]
    [Arguments("availability", "row", true)]
    [Arguments("availability", "create", false)]
    [Arguments("availability", "command-validation", false)]
    [Arguments("availability", "command-create", false)]
    [Arguments("availability", "interpret", false)]
    [Arguments("availability", "reader-cleanup", false)]
    [Arguments("availability", "command-cleanup", false)]
    [Arguments("availability", "session-cleanup", false)]
    [Arguments("database", "open", false)]
    [Arguments("database", "dispatch", false)]
    [Arguments("table", "open", false)]
    [Arguments("table", "row", false)]
    public async Task ExistenceProbe_OnlyClassifiedAvailabilityFailuresBecomeFalse(string kind, string phase, bool mapsToFalse)
    {
        var harness = new ExistenceProbeHarness();
        var expected = new ProbeConnectionFailure();
        switch (phase)
        {
            case "create": harness.Creating = () => throw expected; break;
            case "command-validation": harness.Commands.ValidationFailure = expected; break;
            case "command-create": harness.Commands.Creating = () => throw expected; break;
            case "open": harness.Session.Opening = ProbeFault(expected); break;
            case "dispatch": harness.Access = new(ProbeFault(expected)); harness.Session.Access = harness.Access; break;
            case "row": harness.Shape = "reader"; harness.Reader.Advance = ProbeFault(expected); break;
            case "interpret": harness.Interpret = _ => throw expected; break;
            case "reader-cleanup": harness.Shape = "reader"; harness.Reader.Cleanup = ProbeFault(expected); break;
            case "command-cleanup": harness.Commands.Resource.Cleanup = ProbeFault(expected); break;
            case "session-cleanup": harness.Session.Cleanup = ProbeFault(expected); break;
        }
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        if (mapsToFalse) await Assert.That(await RunProbe(provider, kind)).IsFalse();
        else
        {
            await Assert.That(await AsyncEnumerationFailureOf(() => RunProbe(provider, kind))).IsSameReferenceAs(expected);
            var context = ExecutionFailureContexts.Get(expected)!;
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
            await Assert.That(context.TransactionId).IsNull();
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        }
        await Assert.That(harness.Session.Disposals).IsEqualTo(phase == "create" ? 0 : 1);
        await Assert.That(harness.Classifications).IsEqualTo(mapsToFalse ? 1 : 0);
    }

    [Test]
    [Arguments("unsupported")]
    [Arguments("argument")]
    [Arguments("disposed")]
    [Arguments("canceled")]
    public async Task ExistenceProbe_ContractAndCancellationFailuresCannotBeMisclassifiedAsUnavailable(string error)
    {
        Exception expected = error switch
        {
            "unsupported" => new NotSupportedException(),
            "argument" => new ArgumentException(),
            "disposed" => new ObjectDisposedException("probe"),
            _ => new OperationCanceledException(new CancellationToken(true))
        };
        var harness = new ExistenceProbeHarness { Classify = _ => true };
        harness.Session.Opening = ProbeFault(expected);
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore())).IsSameReferenceAs(expected);
        await Assert.That(harness.Classifications).IsEqualTo(0);
        await Assert.That(ExecutionFailureContexts.Get(expected)!.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
    }

    [Test]
    [Arguments("open")]
    [Arguments("dispatch")]
    [Arguments("row")]
    [Arguments("reader-cleanup")]
    [Arguments("command-cleanup")]
    [Arguments("session-cleanup")]
    public async Task ExistenceProbe_CancellationNeverBecomesFalseAndCleanupSettlesIndependently(string phase)
    {
        var harness = new ExistenceProbeHarness(false) { Classify = _ => true };
        var pause = new AsyncCheckpoint(paused: true);
        switch (phase)
        {
            case "open": harness.Session.Opening = pause; break;
            case "dispatch": harness.Access = new(pause); harness.Session.Access = harness.Access; break;
            case "row": harness.Shape = "reader"; harness.Reader.Advance = pause; break;
            case "reader-cleanup": harness.Shape = "reader"; harness.Reader.Cleanup = pause; break;
            case "command-cleanup": harness.Commands.Resource.Cleanup = pause; break;
            case "session-cleanup": harness.Session.Cleanup = pause; break;
        }
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.FileOrServerExistsAsyncCore(cancellation.Token);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        if (phase.EndsWith("cleanup", StringComparison.Ordinal))
        {
            try
            {
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(pause.ObservedToken).IsEqualTo(CancellationToken.None);
            }
            finally { pause.Release(); }
        }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(harness.Classifications).IsEqualTo(0);
    }

    [Test]
    public async Task ExistenceProbe_NonCooperativeOpeningCannotBeAbandoned()
    {
        var harness = new ExistenceProbeHarness();
        harness.Session.Opening = new(paused: true);
        harness.Session.IgnoreCancellation = true;
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var pending = provider.DatabaseExistsAsyncCore(token: cancellation.Token);
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(harness.Session.Disposals).IsEqualTo(0);
        }
        finally { harness.Session.Opening.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending) is OperationCanceledException).IsTrue();
        await Assert.That(harness.Commands.Creates).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExistenceProbe_CleanupFailuresPreventFalseAndRetainOriginalOrder(bool sameException)
    {
        var harness = new ExistenceProbeHarness { Shape = "reader" };
        var primary = new ProbeConnectionFailure();
        var reader = sameException ? primary : new ProbeConnectionFailure();
        var command = sameException ? primary : new ProbeConnectionFailure();
        var session = sameException ? primary : new ProbeConnectionFailure();
        harness.Reader.Advance = ProbeFault(primary);
        harness.Reader.Cleanup = ProbeFault(reader);
        harness.Commands.Resource.Cleanup = ProbeFault(command);
        harness.Session.Cleanup = ProbeFault(session);
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore())).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Select(x => x.Exception).SequenceEqual(sameException ? [] : new Exception[] { reader, command, session })).IsTrue();
        await Assert.That(harness.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(harness.Classifications).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExistenceProbe_ClassificationCannotHideItsOwnFailureOrObservedCancellation(bool cancel)
    {
        var harness = new ExistenceProbeHarness();
        var primary = new ProbeConnectionFailure();
        var classification = new InvalidOperationException("classifier failed");
        harness.Session.Opening = ProbeFault(primary);
        using var cancellation = new CancellationTokenSource();
        harness.Classify = _ => { if (!cancel) throw classification; cancellation.Cancel(); return true; };
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore(cancellation.Token))).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(cancel ? 0 : 1);
        if (!cancel) await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(classification);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    private static Task<bool> RunProbe(IDatabaseProvider provider, string kind, CancellationToken token = default) => kind switch
    {
        "availability" => provider.FileOrServerExistsAsyncCore(token),
        "database" => provider.DatabaseExistsAsyncCore(token: token),
        _ => provider.TableExistsAsyncCore("table", token: token)
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExistenceProbe_ReportedCancellationCannotBecomeUnavailableWithoutARequestToken(bool opening)
    {
        var harness = new ExistenceProbeHarness { Classify = _ => true };
        var primary = new ProbeConnectionFailure();
        var fault = ProbeFault(primary);
        fault.ReportingFailure = failure => ExecutionFailureContexts.Attach(failure, new(ExecutionFailureCause.Cancellation,
            opening ? ExecutionFailureStage.Initialization : ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, []));
        if (opening) harness.Session.Opening = fault;
        else { harness.Access = new(fault); harness.Session.Access = harness.Access; }
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore())).IsSameReferenceAs(primary);
        await Assert.That(harness.Classifications).IsEqualTo(0);
        await Assert.That(ExecutionFailureContexts.Get(primary)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExistenceProbe_UnexpectedProviderAndLocalFailuresRemainExceptions(bool local)
    {
        var harness = new ExistenceProbeHarness { Shape = local ? "local" : "scalar" };
        var expected = new InvalidOperationException("unexpected probe failure");
        if (local) harness.LocalRead = () => throw expected;
        else harness.Session.Opening = ProbeFault(expected);
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        await Assert.That(await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore())).IsSameReferenceAs(expected);
        await Assert.That(harness.Classifications).IsEqualTo(local ? 0 : 1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(local ? 0 : 1);
    }

    private static AsyncCheckpoint ProbeFault(Exception failure)
    { var checkpoint = new AsyncCheckpoint(paused: true); checkpoint.Fail(failure); return checkpoint; }

    private sealed class ProbeConnectionFailure : Exception;

    private sealed class LegacyExistenceProbeProvider() : ScriptedMutationProvider<TransactionMutationGuardDb>(new())
    {
        internal int SyncCalls { get; private set; }
        public override bool FileOrServerExists() { SyncCalls++; return true; }
        public override bool DatabaseExists(string? databaseName = null) { SyncCalls++; return true; }
        public override bool TableExists(string tableName, string? databaseName = null) { SyncCalls++; return true; }
    }

    private sealed class ExistenceProbeProvider(ScriptedMutationScenario scenario, Func<ExistenceProbeHarness> create)
        : ScriptedMutationProvider<TransactionMutationGuardDb>(scenario), IAsyncExistenceProbeSource
    {
        internal int Captures { get; private set; }
        internal void SetEffectiveName(string name) => DatabaseName = name;
        public ExistenceProbePlan CaptureExistenceProbe(ExistenceProbeRequest request)
        {
            Captures++;
            return create().Capture(request, DatabaseName);
        }
    }

    private sealed class ExistenceProbeHarness
    {
        internal ExistenceProbeHarness(bool exists = true)
        {
            Reader = new(exists ? [11, 22] : []);
            Access = new() { Reader = Reader, ScalarResult = exists ? 1 : null };
            LocalRead = () => exists;
            Session = new() { Access = Access, CommandFactory = Commands };
        }
        internal string Shape { get; set; } = "scalar";
        internal ExistenceProbeRequest? Request { get; private set; }
        internal ControlledAsyncDataReader Reader { get; }
        internal ControlledAsyncDatabaseAccess Access { get; set; }
        internal ControlledOwnedCommandFactory Commands { get; } = new();
        internal ExistenceProbeSession Session { get; }
        internal Func<IAsyncExistenceProbeSession>? Creating { get; set; }
        internal Func<object?, bool> Interpret { get; set; } = value => value is not null;
        internal Func<bool> LocalRead { get; set; }
        internal Func<Exception, bool> Classify { get; set; } = failure => failure is ProbeConnectionFailure;
        internal Exception? ValidationFailure { get; set; }
        internal int Validations { get; private set; }
        internal int Creates { get; private set; }
        internal int LocalReads { get; private set; }
        internal int Classifications { get; private set; }
        internal ExistenceProbePlan Capture(ExistenceProbeRequest request, string effectiveDatabase)
        {
            Request = request;
            var database = request.DatabaseName ?? effectiveDatabase;
            var text = database + "/" + request.TableName;
            Commands.Creating ??= () => { Commands.Resource.Borrowed.CommandText = text; return Commands.Resource; };
            var create = Creating;
            var local = LocalRead;
            var classify = Classify;
            IAsyncExistenceProbeSession Create() { Creates++; return create?.Invoke() ?? Session; }
            bool ClassifyFailure(Exception failure) { Classifications++; return classify(failure); }
            return Shape switch
            {
                "local" => ExistenceProbePlan.Local(Validate, () => { LocalReads++; return local(); }),
                "reader" => ExistenceProbePlan.Reader(Validate, Create, ClassifyFailure),
                _ => ExistenceProbePlan.Scalar(Validate, Create, Interpret, ClassifyFailure)
            };
        }
        private void Validate() { Validations++; if (ValidationFailure is not null) throw ValidationFailure; }
    }

    private sealed class ExistenceProbeSession : IAsyncExistenceProbeSession
    {
        public IAsyncDatabaseAccess Access { get; set; } = null!;
        public IAsyncOwnedCommandFactory CommandFactory { get; set; } = null!;
        internal AsyncCheckpoint Opening { get; set; } = new();
        internal AsyncCheckpoint Cleanup { get; set; } = new();
        internal int Opens { get; private set; }
        internal int Disposals { get; private set; }
        internal bool IgnoreCancellation { get; set; }
        public async Task OpenAsync(CancellationToken token)
        { Opens++; await Opening.ReachAsync(IgnoreCancellation ? CancellationToken.None : token); }
        public async ValueTask DisposeAsync() { Disposals++; await Cleanup.ReachAsync(CancellationToken.None); }
    }
}
