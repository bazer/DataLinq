using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(null, 17)]
    [Arguments(0L, 0)]
    [Arguments(1L, 1)]
    [Arguments(21_474_830_000_000L, 2_147_483)]
    public async Task SyncMetadata_CommandsUseCapturedTimeoutAndSettleBeforeNextCommand(long? ticks, int expected)
    {
        var harness = new SyncMetadataHarness();
        using var provider = new SyncMetadataProvider(harness);
        var thread = Environment.CurrentManagedThreadId;
        var result = provider.ReadValidationMetadataCore(ticks is { } value ? TimeSpan.FromTicks(value) : null).ValueOrException();
        await Assert.That(result.IsFrozen).IsTrue();
        await Assert.That(result.TableModels).IsEmpty();
        await Assert.That(harness.Events.SequenceEqual(new[] { "validate", "open", "create:rows", "reader", "reader-dispose", "command-dispose:rows", "create:scalar", "scalar", "command-dispose:scalar", "session-dispose" })).IsTrue();
        await Assert.That(harness.Threads.All(id => id == thread)).IsTrue();
        await Assert.That(harness.Commands.All(command => command.CommandTimeout == expected && command.DisposeCalls == 1)).IsTrue();
        await Assert.That(harness.Reader.AsyncReadCalls).IsEqualTo(0);
        await Assert.That(harness.Reader.AsyncDisposeCalls).IsEqualTo(0);
        await Assert.That(harness.Reader.SyncCalls).IsEqualTo(4);
        await Assert.That(harness.Disposals).IsEqualTo(1);
        await Assert.That(() => harness.Context!.ExecuteScalar(new Sql("late"))).Throws<InvalidOperationException>();
        await Assert.That(harness.Commands.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(21_474_830_000_001L)]
    public async Task SyncMetadata_InvalidTimeoutPrecedesCaptureAndAnyOwnedWork(long ticks)
    {
        var harness = new SyncMetadataHarness();
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(() => provider.ReadValidationMetadataCore(TimeSpan.FromTicks(ticks))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(provider.Captures).IsEqualTo(0);
        await Assert.That(harness.Events).IsEmpty();
    }

    [Test]
    public async Task SyncMetadata_UnsupportedProviderDoesNotFallBackToImportOrAsync()
    {
        using var provider = new ScriptedMutationProvider<TransactionMutationGuardDb>(new());
        await Assert.That(() => provider.ReadValidationMetadataCore()).Throws<NotSupportedException>();
    }

    [Test]
    public async Task SyncMetadata_CapabilityValidationSettlesCreatedSessionBeforeOpening()
    {
        var failure = new NotSupportedException("timeout unsupported");
        var harness = new SyncMetadataHarness { ValidationFailure = failure };
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore(TimeSpan.FromSeconds(1)))).IsSameReferenceAs(failure);
        await Assert.That(harness.Events.SequenceEqual(new[] { "validate", "session-dispose" })).IsTrue();
    }

    [Test]
    [Arguments("open")]
    [Arguments("create")]
    [Arguments("dispatch")]
    [Arguments("parse")]
    public async Task SyncMetadata_OperationalFailureNeverPublishesMetadataAndSettlesResources(string boundary)
    {
        var original = new InvalidOperationException(boundary);
        var harness = new SyncMetadataHarness();
        switch (boundary)
        {
            case "open": harness.Opening = () => throw original; break;
            case "create": harness.Creating = () => throw original; break;
            case "dispatch": harness.Dispatching = () => throw original; break;
            case "parse": harness.Materializing = () => throw original; break;
        }
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore())).IsSameReferenceAs(original);
        await Assert.That(harness.Disposals).IsEqualTo(1);
        await Assert.That(harness.Commands.All(command => command.DisposeCalls == 1)).IsTrue();
        await Assert.That(ExecutionFailureContexts.Get(original)!.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(ExecutionFailureContexts.Get(original)!.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        if (boundary == "parse") await Assert.That(harness.Reader.IsDisposed).IsTrue();
    }

    [Test]
    public async Task SyncMetadata_PrimaryFailureRetainsOrderedReaderCommandAndSessionCleanupFailures()
    {
        var original = new InvalidOperationException("materialization");
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var sessionCleanup = new Exception("session cleanup");
        var harness = new SyncMetadataHarness
        {
            Materializing = () => throw original,
            CommandCleanupFailure = commandCleanup,
            SessionCleanupFailure = sessionCleanup
        };
        harness.Reader.SyncDisposalFailure = readerCleanup;
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore())).IsSameReferenceAs(original);
        var context = ExecutionFailureContexts.Get(original)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.SecondaryFailures.Select(failure => failure.Exception).SequenceEqual(new[] { readerCleanup, commandCleanup, sessionCleanup })).IsTrue();
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(harness.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task SyncMetadata_SwallowedCommandFailureStillPreventsSuccessfulMetadataPublication()
    {
        var original = new Exception("dispatch failure");
        var harness = new SyncMetadataHarness { Dispatching = () => throw original };
        harness.Parse = context =>
        {
            try { context.ExecuteScalar(new Sql("suppressed")); }
            catch (Exception) { }
            return SyncMetadataEmpty();
        };
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore())).IsSameReferenceAs(original);
        await Assert.That(harness.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task SyncMetadata_ReentrantCommandIsRejectedAndPoisonedEvenWhenParserSuppressesIt()
    {
        var harness = new SyncMetadataHarness();
        harness.Dispatching = () =>
        {
            try { harness.Context!.ExecuteScalar(new Sql("reentrant")); }
            catch (InvalidOperationException) { }
        };
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore())).IsTypeOf<InvalidOperationException>();
        await Assert.That(harness.Commands.Count).IsEqualTo(1);
        await Assert.That(harness.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task SyncMetadata_FailedOptionAndLaterCleanupFailureBothRemainInspectable()
    {
        var original = DLOptionFailure.Fail(DLFailureType.InvalidModel, "incomplete metadata");
        var cleanup = new Exception("session cleanup");
        var harness = new SyncMetadataHarness { Parse = _ => original, SessionCleanupFailure = cleanup };
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(provider.ReadValidationMetadataCore().TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.InnerFailures[0]).IsSameReferenceAs(original);
        await Assert.That(failure.InnerFailures[1].FailureValue).IsSameReferenceAs(cleanup);
        await Assert.That(harness.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task SyncMetadata_CancellationExceptionRetainsIdentityAndSettledCleanupEvidence()
    {
        var original = new OperationCanceledException(new CancellationToken(true));
        var cleanup = new Exception("session cleanup");
        var harness = new SyncMetadataHarness { Opening = () => throw original, SessionCleanupFailure = cleanup };
        using var provider = new SyncMetadataProvider(harness);
        var observed = await Assert.That(() => provider.ReadValidationMetadataCore()).Throws<OperationCanceledException>();
        await Assert.That(observed).IsSameReferenceAs(original);
        await Assert.That(ExecutionFailureContexts.Get(original)!.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(harness.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task SyncMetadata_UndefinedResultIsNotAReadableEmptySchema()
    {
        var harness = new SyncMetadataHarness { Parse = _ => default };
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore())).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task SyncMetadata_CleanupFailurePreventsPublishingOtherwiseCompleteMetadata()
    {
        var cleanup = new Exception("session cleanup");
        var harness = new SyncMetadataHarness { SessionCleanupFailure = cleanup };
        using var provider = new SyncMetadataProvider(harness);
        await Assert.That(SyncMetadataFailure(provider.ReadValidationMetadataCore())).IsSameReferenceAs(cleanup);
        await Assert.That(ExecutionFailureContexts.Get(cleanup)!.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(harness.Reader.IsDisposed).IsTrue();
        await Assert.That(harness.Commands.All(command => command.DisposeCalls == 1)).IsTrue();
    }

    private static Exception SyncMetadataFailure(Option<DatabaseDefinition, IDLOptionFailure> result) =>
        !result.TryUnwrap(out _, out var failure) && failure.FailureValue is Exception exception
            ? exception : throw new InvalidOperationException("Expected operational failure.");

    private static Option<DatabaseDefinition, IDLOptionFailure> SyncMetadataEmpty() =>
        new MetadataDefinitionFactory().BuildProviderMetadata(new MetadataDatabaseDraft("sync", new("SyncDb", "Tests", ModelCsType.Class)) { DbName = "effective" });

    private sealed class SyncMetadataProvider(SyncMetadataHarness harness) : ScriptedMutationProvider<TransactionMutationGuardDb>(new()), ISyncProviderMetadataSource
    {
        internal int Captures;
        public ISyncMetadataReadPlan CaptureValidationMetadata(MetadataReadSettings settings)
        {
            Captures++;
            return new SyncMetadataPlan(harness);
        }
    }

    private sealed class SyncMetadataPlan(SyncMetadataHarness harness) : ISyncMetadataReadPlan
    {
        public void Validate() { }
        public ISyncMetadataSession CreateSession() => harness;
        public Option<DatabaseDefinition, IDLOptionFailure> Read(SyncMetadataReadContext context)
        {
            harness.Context = context;
            if (harness.Parse is { } parse) return parse(context);
            _ = context.Read(new Sql("rows"), row => { harness.Materializing?.Invoke(); return row.GetInt32(0); });
            _ = context.ExecuteScalar(new Sql("scalar"));
            return SyncMetadataEmpty();
        }
    }

    private sealed class SyncMetadataHarness : ISyncMetadataSession
    {
        internal readonly List<string> Events = [];
        internal readonly List<int> Threads = [];
        internal readonly List<SyncMetadataCommand> Commands = [];
        internal readonly ControlledAsyncDataReader Reader = new() { AllowSynchronousCalls = true };
        internal SyncMetadataReadContext? Context;
        internal Action? Opening, Creating, Dispatching, Materializing;
        internal Exception? ValidationFailure, CommandCleanupFailure, SessionCleanupFailure;
        internal Func<SyncMetadataReadContext, Option<DatabaseDefinition, IDLOptionFailure>>? Parse;
        internal int Disposals;
        private void Record(string text) { Events.Add(text); Threads.Add(Environment.CurrentManagedThreadId); }
        public void Validate(int? seconds) { Record("validate"); if (ValidationFailure is { } failure) throw failure; }
        public void Open() { Record("open"); Opening?.Invoke(); }
        public IDbCommand CreateCommand(CapturedSql sql)
        {
            Record("create:" + sql.ToSql().Text);
            Creating?.Invoke();
            var command = new SyncMetadataCommand(() => Record("command-dispose:" + sql.ToSql().Text), CommandCleanupFailure)
                { CommandText = sql.ToSql().Text, CommandTimeout = 17 };
            Commands.Add(command);
            return command;
        }
        public IDataLinqDataReader ExecuteReader(IDbCommand command)
        {
            Record("reader");
            Dispatching?.Invoke();
            Reader.SyncDisposing = () => Record("reader-dispose");
            return Reader;
        }
        public object? ExecuteScalar(IDbCommand command) { Record("scalar"); Dispatching?.Invoke(); return 1; }
        public void Dispose() { Disposals++; Record("session-dispose"); if (SessionCleanupFailure is { } failure) throw failure; }
    }

    private sealed class SyncMetadataCommand(Action disposing, Exception? cleanupFailure) : ProbeDbCommand
    {
        protected override void Dispose(bool release)
        {
            base.Dispose(release);
            if (!release) return;
            disposing();
            if (cleanupFailure is not null) throw cleanupFailure;
        }
    }
}
