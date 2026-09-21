using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
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
    [Arguments(null, null)]
    [Arguments(0L, 0)]
    [Arguments(1L, 1)]
    [Arguments(10_000_000L, 1)]
    [Arguments(10_000_001L, 2)]
    [Arguments(21_474_830_000_000L, 2_147_483)]
    public async Task MetadataRead_TimeoutNormalizationUsesWholeSecondsAndPreservesDefault(long? ticks, int? expected)
    {
        await Assert.That(MetadataReadSettings.NormalizeTimeout(ticks is { } value ? TimeSpan.FromTicks(value) : null)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(-10_000L)]
    [Arguments(21_474_830_000_001L)]
    [Arguments(long.MaxValue)]
    public async Task MetadataRead_InvalidTimeoutPrecedesCaptureCancellationAndIO(long ticks)
    {
        var harness = new MetadataHarness();
        using var provider = new MetadataTestProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.ReadValidationMetadataAsyncCore(TimeSpan.FromTicks(ticks), token: new(true)));
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(provider.Captures).IsEqualTo(0);
        await Assert.That(harness.Session.Opens).IsEqualTo(0);
    }

    [Test]
    [Arguments(null, 17)]
    [Arguments(0L, 0)]
    [Arguments(1L, 1)]
    [Arguments(21_474_830_000_000L, 2_147_483)]
    public async Task MetadataRead_AllCommandsReceiveCapturedTimeoutAfterEarlierCleanup(long? ticks, int expected)
    {
        var harness = new MetadataHarness();
        using var provider = new MetadataTestProvider(new(), () => harness);
        using var cancellation = new CancellationTokenSource();
        var result = await provider.ReadValidationMetadataAsyncCore(ticks is { } value ? TimeSpan.FromTicks(value) : null, token: cancellation.Token);
        var metadata = result.ValueOrException();
        await Assert.That(metadata.IsFrozen).IsTrue();
        await Assert.That(metadata.TableModels.Select(x => x.Table.DbName).SequenceEqual(new[] { "table_11", "table_22" })).IsTrue();
        await Assert.That(metadata.TableModels.All(x => x.Table.Columns.Length == 1)).IsTrue();
        await Assert.That(harness.Executed.SequenceEqual(new[] { "tables", "columns:11", "ddl:11", "columns:22", "ddl:22" })).IsTrue();
        foreach (var query in harness.Queries.Values)
        {
            await Assert.That(query.Command.Resource.Borrowed.CommandTimeout).IsEqualTo(expected);
            await Assert.That(query.Access.Dispatch.ObservedToken).IsEqualTo(cancellation.Token);
            if (!query.Scalar) await Assert.That(query.Reader.Advance.ObservedToken).IsEqualTo(cancellation.Token);
            await Assert.That(query.Reader.AsyncDisposeCalls).IsEqualTo(query.Scalar ? 0 : 1);
            await Assert.That(query.Command.Resource.AsyncDisposals).IsEqualTo(1);
            await Assert.That(query.Reader.SyncCalls).IsEqualTo(0);
            await Assert.That(query.Command.Resource.Borrowed.SyncExecutionCalls).IsEqualTo(0);
        }
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(harness.Session.Opening.ObservedToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task MetadataRead_ImportOptionsAndLoggerAreCapturedBeforeSuspension()
    {
        var harness = new MetadataHarness();
        harness.Session.Opening = new(paused: true);
        var firstLog = 0;
        var secondLog = 0;
        var include = new List<string> { "table_11" };
        var factory = new MetadataTestFactory(() => harness)
        {
            Options = new() { CapitaliseNames = true, DeclareEnumsInClass = true, Include = include, Log = _ => firstLog++ }
        };
        var pending = ImportMetadata(factory);
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        include[0] = "table_22";
        factory.Options = new() { Include = ["table_22"], Log = _ => secondLog++ };
        harness.Session.Opening.Release();
        var metadata = (await pending).ValueOrException();
        await Assert.That(metadata.TableModels.Single().Table.DbName).IsEqualTo("table_11");
        var settings = factory.Request!.Settings;
        await Assert.That(settings.Purpose).IsEqualTo(MetadataReadPurpose.Import);
        await Assert.That(settings.CapitaliseNames).IsTrue();
        await Assert.That(settings.DeclareEnumsInClass).IsTrue();
        await Assert.That(settings.Include.Single()).IsEqualTo("table_11");
        await Assert.That(Capture<NotSupportedException>(() => ((IList<string>)settings.Include)[0] = "mutated")).IsTypeOf<NotSupportedException>();
        await Assert.That(firstLog).IsEqualTo(1);
        await Assert.That(secondLog).IsEqualTo(0);
        await Assert.That(factory.SyncReads).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataRead_RuntimeEmptySchemaIsDistinctFromImportAndReadFailure(bool import)
    {
        var harness = new MetadataHarness([]);
        using var provider = new MetadataTestProvider(new(), () => harness);
        var result = await (import ? ImportMetadata(new MetadataTestFactory(() => harness)) : provider.ReadValidationMetadataAsyncCore());
        if (import) await Assert.That(result.HasFailed).IsTrue();
        else
        {
            await Assert.That(result.ValueOrException().TableModels.Length).IsEqualTo(0);
            await Assert.That(result.ValueOrException().IsFrozen).IsTrue();
            await Assert.That(harness.Settings!.Purpose).IsEqualTo(MetadataReadPurpose.RuntimeValidation);
            await Assert.That(harness.Settings.Include.Count).IsEqualTo(0);
        }
        await Assert.That(harness.Executed.SequenceEqual(new[] { "tables" })).IsTrue();
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_RuntimeUsesCapturedProviderIdentityAndFreshIndependentSessions()
    {
        var first = new MetadataHarness([]);
        var second = new MetadataHarness([11]);
        first.Session.Opening = new(paused: true);
        var queue = new Queue<MetadataHarness>([first, second]);
        var scenario = new ScriptedMutationScenario();
        using var provider = new MetadataTestProvider(scenario, queue.Dequeue);
        provider.SetEffectiveName("normalized-shared-memory");
        using var transaction = provider.StartTransaction();
        var pending = provider.ReadValidationMetadataAsyncCore();
        await first.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        provider.SetEffectiveName("different-identity");
        first.Session.Opening.Release();
        var empty = (await pending).ValueOrException();
        var populated = (await provider.ReadValidationMetadataAsyncCore()).ValueOrException();
        await Assert.That(empty.DbName).IsEqualTo("normalized-shared-memory");
        await Assert.That(populated.DbName).IsEqualTo("different-identity");
        await Assert.That(empty.TableModels.Length).IsEqualTo(0);
        await Assert.That(populated.TableModels.Length).IsEqualTo(1);
        await Assert.That(provider.Captures).IsEqualTo(2);
        await Assert.That(scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(scenario.Commits).IsEqualTo(0);
        await Assert.That(scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(scenario.Disposals).IsEqualTo(0);
        await Assert.That(first.Session.Disposals).IsEqualTo(1);
        await Assert.That(second.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataRead_UnsupportedLegacyCapabilityPrecedesCancellationWithoutFallback(bool import)
    {
        using var provider = new ScriptedMutationProvider(new());
        var factory = new LegacyMetadataTestFactory();
        var failure = await AsyncEnumerationFailureOf(() => import ? ImportMetadata(factory, new(true)) : provider.ReadValidationMetadataAsyncCore(token: new(true)));
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(factory.SyncReads).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataRead_UnsupportedTimeoutAndPreCancellationPerformNoOpen(bool unsupported)
    {
        var harness = new MetadataHarness { SupportsTimeout = !unsupported };
        using var provider = new MetadataTestProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.ReadValidationMetadataAsyncCore(TimeSpan.Zero, token: new(true)));
        if (unsupported) await Assert.That(failure).IsTypeOf<NotSupportedException>();
        else await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(harness.Session.Opens).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(0);
        await Assert.That(harness.Queries.Values.Sum(x => x.Command.Creates)).IsEqualTo(0);
    }

    [Test]
    [Arguments("open")]
    [Arguments("query-capture")]
    [Arguments("command-create")]
    [Arguments("acquire")]
    [Arguments("row")]
    [Arguments("materialize")]
    [Arguments("reader-cleanup")]
    [Arguments("command-cleanup")]
    [Arguments("scalar")]
    [Arguments("scalar-cleanup")]
    [Arguments("session-cleanup")]
    public async Task MetadataRead_OperationalFailuresRemainOriginalFailedOptionsNeverEmptySchemas(string phase)
    {
        var harness = new MetadataHarness();
        var first = harness.Queries["tables"];
        var expected = new InvalidOperationException(phase);
        var fault = new AsyncCheckpoint(paused: true);
        fault.Fail(expected);
        switch (phase)
        {
            case "open": harness.Session.Opening = fault; break;
            case "query-capture": harness.CaptureOverride = _ => throw expected; break;
            case "command-create": harness.CaptureOverride = _ => new ControlledOwnedCommandFactory { Creating = () => throw expected }; break;
            case "acquire": first.Access = new(fault) { Reader = first.Reader }; break;
            case "row": first.Reader.Advance = fault; break;
            case "materialize": harness.Materializing = () => throw expected; break;
            case "reader-cleanup": first.Reader.Cleanup = fault; break;
            case "command-cleanup": first.Command.Resource.Cleanup = fault; break;
            case "scalar": harness.Queries["ddl:11"].Access = new(fault); break;
            case "scalar-cleanup": harness.Queries["ddl:11"].Command.Resource.Cleanup = fault; break;
            case "session-cleanup": harness.Session.Cleanup = fault; break;
        }
        var result = await ImportMetadata(new MetadataTestFactory(() => harness));
        await Assert.That(MetadataException(result)).IsSameReferenceAs(expected);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        if (phase.StartsWith("scalar", StringComparison.Ordinal))
        {
            await Assert.That(harness.Executed.SequenceEqual(new[] { "tables", "columns:11", "ddl:11" })).IsTrue();
            await Assert.That(context.Stage).IsEqualTo(phase == "scalar" ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Cleanup);
            await Assert.That(harness.Queries["ddl:11"].Command.Resource.AsyncDisposals).IsEqualTo(1);
        }
        else if (phase != "session-cleanup") await Assert.That(harness.Executed.All(x => x == "tables")).IsTrue();
    }

    [Test]
    [Arguments("open")]
    [Arguments("row")]
    [Arguments("reader-cleanup")]
    [Arguments("scalar")]
    [Arguments("scalar-cleanup")]
    [Arguments("session-cleanup")]
    public async Task MetadataRead_CancellationEscapesAndOwnedCleanupNeverInheritsCanceledToken(string phase)
    {
        var harness = new MetadataHarness();
        var checkpoint = new AsyncCheckpoint(paused: true);
        var first = harness.Queries["tables"];
        switch (phase)
        {
            case "open": harness.Session.Opening = checkpoint; break;
            case "row": first.Reader.Advance = checkpoint; break;
            case "reader-cleanup": first.Reader.Cleanup = checkpoint; break;
            case "scalar": harness.Queries["ddl:11"].Access = new(checkpoint); break;
            case "scalar-cleanup": harness.Queries["ddl:11"].Command.Resource.Cleanup = checkpoint; break;
            case "session-cleanup": harness.Session.Cleanup = checkpoint; break;
        }
        using var cancellation = new CancellationTokenSource();
        var pending = ImportMetadata(new MetadataTestFactory(() => harness), cancellation.Token);
        await checkpoint.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        if (phase.EndsWith("cleanup", StringComparison.Ordinal))
        {
            try
            {
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(checkpoint.ObservedToken).IsEqualTo(CancellationToken.None);
            }
            finally { checkpoint.Release(); }
        }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(harness.Session.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        if (phase == "reader-cleanup") await Assert.That(harness.Executed.SequenceEqual(new[] { "tables" })).IsTrue();
        if (phase.StartsWith("scalar", StringComparison.Ordinal))
        {
            await Assert.That(harness.Executed.SequenceEqual(new[] { "tables", "columns:11", "ddl:11" })).IsTrue();
            await Assert.That(harness.Queries["ddl:11"].Command.Resource.AsyncDisposals).IsEqualTo(1);
            await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(phase == "scalar" ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Materialization);
        }
    }

    [Test]
    public async Task MetadataRead_SwallowedQueryFailureCannotPublishPartialSuccessOrRetry()
    {
        var harness = new MetadataHarness();
        var expected = new InvalidOperationException("failed second metadata query");
        harness.Queries["columns:11"].Reader.Advance = new(paused: true);
        harness.Queries["columns:11"].Reader.Advance.Fail(expected);
        harness.Parser = async (context, token) =>
        {
            try { await harness.ReadDefault(context, token); } catch { }
            await AsyncEnumerationFailureOf(() => context.ReadAsync(new Sql("tables"), r => r.GetInt32(0)));
            return harness.EmptyDefinition();
        };
        var result = await ImportMetadata(new MetadataTestFactory(() => harness));
        await Assert.That(MetadataException(result)).IsSameReferenceAs(expected);
        await Assert.That(harness.Executed.SequenceEqual(new[] { "tables", "columns:11" })).IsTrue();
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task MetadataRead_UnfinishedParserCommandIsDrainedWithoutPublishingItsResult(bool commandFails, bool scalar)
    {
        var harness = new MetadataHarness();
        var dispatch = new AsyncCheckpoint(paused: true);
        var first = harness.Queries[scalar ? "ddl:11" : "tables"];
        first.Access = new(dispatch) { Reader = first.Reader };
        Task? abandoned = null;
        harness.Parser = (context, _) =>
        {
            abandoned = scalar ? context.ExecuteScalarAsync(new Sql("ddl:11")) : context.ReadAsync(new Sql("tables"), r => r.GetInt32(0));
            return Task.FromResult(harness.EmptyDefinition());
        };
        var pending = ImportMetadata(new MetadataTestFactory(() => harness));
        await dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new InvalidOperationException("abandoned query failed");
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(harness.Session.Disposals).IsEqualTo(0);
            await Assert.That(await AsyncEnumerationFailureOf(() => harness.Context!.ReadAsync(new Sql("tables"), r => r.GetInt32(0)))).IsTypeOf<InvalidOperationException>();
        }
        finally { if (commandFails) dispatch.Fail(expected); else dispatch.Release(); }
        var failure = MetadataException(await pending);
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(commandFails ? 1 : 0);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.InvalidOperation);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        if (commandFails)
        {
            await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(expected);
            await AsyncEnumerationFailureOf(() => abandoned!);
        }
        else await abandoned!;
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(first.Command.Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_QueryAndAllCleanupFailuresRetainEncounterOrder()
    {
        var harness = new MetadataHarness();
        var first = harness.Queries["tables"];
        var errors = Enumerable.Range(0, 4).Select(i => new InvalidOperationException("failure " + i)).ToArray();
        first.Reader.Advance = Faulted(errors[0]);
        first.Reader.Cleanup = Faulted(errors[1]);
        first.Command.Resource.Cleanup = Faulted(errors[2]);
        harness.Session.Cleanup = Faulted(errors[3]);
        var original = MetadataException(await ImportMetadata(new MetadataTestFactory(() => harness)));
        await Assert.That(original).IsSameReferenceAs(errors[0]);
        var context = ExecutionFailureContexts.Get(original)!;
        await Assert.That(context.SecondaryFailures.Select(x => x.Exception).SequenceEqual(errors.Skip(1))).IsTrue();
        await Assert.That(first.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(first.Command.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        static AsyncCheckpoint Faulted(Exception failure) { var checkpoint = new AsyncCheckpoint(paused: true); checkpoint.Fail(failure); return checkpoint; }
    }

    [Test]
    public async Task MetadataRead_ScalarAndBothCleanupFailuresRetainEncounterOrder()
    {
        var harness = new MetadataHarness();
        var scalar = harness.Queries["ddl:11"];
        var errors = Enumerable.Range(0, 3).Select(i => new InvalidOperationException("scalar failure " + i)).ToArray();
        scalar.Access = new(Faulted(errors[0]));
        scalar.Command.Resource.Cleanup = Faulted(errors[1]);
        harness.Session.Cleanup = Faulted(errors[2]);
        var original = MetadataException(await ImportMetadata(new MetadataTestFactory(() => harness)));
        await Assert.That(original).IsSameReferenceAs(errors[0]);
        await Assert.That(ExecutionFailureContexts.Get(original)!.SecondaryFailures.Select(x => x.Exception).SequenceEqual(errors.Skip(1))).IsTrue();
        await Assert.That(scalar.Command.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(scalar.Reader.AsyncReadCalls).IsEqualTo(0);
        await Assert.That(scalar.Reader.AsyncDisposeCalls).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
        static AsyncCheckpoint Faulted(Exception failure) { var checkpoint = new AsyncCheckpoint(paused: true); checkpoint.Fail(failure); return checkpoint; }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataRead_DiagnosticOptionFailureIsRetainedAlongsideCleanupFailure(bool cleanupFails)
    {
        var harness = new MetadataHarness();
        var diagnostic = DLOptionFailure.Fail(DLFailureType.InvalidModel, "metadata diagnostic");
        harness.Parser = (_, _) => Task.FromResult<Option<DatabaseDefinition, IDLOptionFailure>>(diagnostic);
        var cleanup = new InvalidOperationException("session cleanup after diagnostic");
        if (cleanupFails)
        {
            harness.Session.Cleanup = new(paused: true);
            harness.Session.Cleanup.Fail(cleanup);
        }
        var result = await ImportMetadata(new MetadataTestFactory(() => harness));
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        if (!cleanupFails) await Assert.That(failure).IsSameReferenceAs(diagnostic);
        else
        {
            await Assert.That(failure.InnerFailures.Length).IsEqualTo(2);
            await Assert.That(failure.InnerFailures[0]).IsSameReferenceAs(diagnostic);
            await Assert.That(failure.InnerFailures[1].FailureValue).IsSameReferenceAs(cleanup);
            await Assert.That(ExecutionFailureContexts.Get(cleanup)!.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        }
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_LoggerFailureRetainsOriginalExceptionAndSettlesSession()
    {
        var harness = new MetadataHarness();
        var expected = new InvalidOperationException("application metadata logger");
        using var provider = new MetadataTestProvider(new(), () => harness);
        await Assert.That(MetadataException(await provider.ReadValidationMetadataAsyncCore(log: _ => throw expected))).IsSameReferenceAs(expected);
        await Assert.That(harness.Executed.Count).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_ForeignCancellationEscapesWithoutInventingRequestAttribution()
    {
        var harness = new MetadataHarness();
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var expected = new OperationCanceledException(foreign.Token);
        harness.Queries["tables"].Reader.Advance = new(paused: true);
        harness.Queries["tables"].Reader.Advance.Fail(expected);
        var failure = await AsyncEnumerationFailureOf(() => ImportMetadata(new MetadataTestFactory(() => harness)));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_TimeoutSetterFailureAfterOwnershipTransferStillDisposesCommand()
    {
        var harness = new MetadataHarness();
        var expected = new InvalidOperationException("command rejected timeout");
        var resource = new RejectingMetadataTimeoutResource(expected);
        var factory = new ControlledOwnedCommandFactory { Creating = () => resource };
        harness.CaptureOverride = _ => factory;
        using var provider = new MetadataTestProvider(new(), () => harness);
        var result = await provider.ReadValidationMetadataAsyncCore(TimeSpan.Zero);
        await Assert.That(MetadataException(result)).IsSameReferenceAs(expected);
        await Assert.That(resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(resource.SyncDisposals).IsEqualTo(0);
        await Assert.That(harness.Executed.Count).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataRead_OverlappingCommandsRejectWithoutClosingTheActiveReader(bool scalar)
    {
        var harness = new MetadataHarness();
        var dispatch = new AsyncCheckpoint(paused: true);
        var first = harness.Queries["tables"];
        first.Access = new(dispatch) { Reader = first.Reader };
        Exception? rejected = null;
        harness.Parser = async (context, _) =>
        {
            var active = context.ReadAsync(new Sql("tables"), r => r.GetInt32(0));
            try
            {
                await dispatch.Entered;
                rejected = await AsyncEnumerationFailureOf(() => scalar ? context.ExecuteScalarAsync(new Sql("ddl:11")) : context.ReadAsync(new Sql("columns:11"), r => r.GetInt32(0)));
                await Assert.That(first.Command.Resource.AsyncDisposals).IsEqualTo(0);
            }
            finally { dispatch.Release(); }
            await active;
            return harness.EmptyDefinition();
        };
        var result = await ImportMetadata(new MetadataTestFactory(() => harness));
        await Assert.That(MetadataException(result)).IsSameReferenceAs(rejected);
        await Assert.That(ExecutionFailureContexts.Get(rejected!)!.Cause).IsEqualTo(ExecutionFailureCause.InvalidOperation);
        await Assert.That(ExecutionFailureContexts.Get(rejected!)!.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(harness.Executed.SequenceEqual(new[] { "tables" })).IsTrue();
        await Assert.That(first.Command.Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_ClosedContextCannotStartMoreQueries()
    {
        var harness = new MetadataHarness();
        (await ImportMetadata(new MetadataTestFactory(() => harness))).ValueOrException();
        var count = harness.Queries.Values.Sum(x => x.Command.Creates);
        var rejected = await AsyncEnumerationFailureOf(() => harness.Context!.ReadAsync(new Sql("tables"), r => r.GetInt32(0)));
        await Assert.That(rejected).IsTypeOf<InvalidOperationException>();
        await Assert.That(harness.Queries.Values.Sum(x => x.Command.Creates)).IsEqualTo(count);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_SessionCollaboratorsAreCapturedBeforeOpeningSuspends()
    {
        var harness = new MetadataHarness();
        var replacement = new MetadataHarness();
        harness.Session.Opening = new(paused: true);
        var pending = ImportMetadata(new MetadataTestFactory(() => harness));
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Session.Access = replacement.Session.Access;
        harness.Session.Commands = replacement;
        harness.Session.Opening.Release();
        await Assert.That((await pending).ValueOrException().TableModels.Length).IsEqualTo(2);
        await Assert.That(harness.Executed.Count).IsEqualTo(5);
        await Assert.That(replacement.Executed.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataRead_MissingDefinitionCannotBecomeSuccessfulMetadata(bool nullSuccess)
    {
        var harness = new MetadataHarness();
        harness.Parser = (_, _) =>
        {
            Option<DatabaseDefinition, IDLOptionFailure> missing = default;
            if (nullSuccess) missing = (DatabaseDefinition)null!;
            return Task.FromResult(missing);
        };
        var result = await ImportMetadata(new MetadataTestFactory(() => harness));
        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataRead_NonCooperativeOpenRemainsOwnedUntilItSettles()
    {
        var harness = new MetadataHarness();
        harness.Session.Opening = new(paused: true);
        harness.Session.IgnoreOpenCancellation = true;
        using var cancellation = new CancellationTokenSource();
        var pending = ImportMetadata(new MetadataTestFactory(() => harness), cancellation.Token);
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
        await Assert.That(harness.Executed.Count).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    private sealed class RejectingMetadataTimeoutResource(Exception failure) : IAsyncOwnedCommand
    {
        public IDbCommand Command { get; } = new RejectingMetadataTimeoutCommand(failure);
        internal int AsyncDisposals { get; private set; }
        internal int SyncDisposals { get; private set; }
        public ValueTask DisposeAsync() { AsyncDisposals++; return default; }
        public void Dispose() => SyncDisposals++;
    }

    private sealed class RejectingMetadataTimeoutCommand(Exception failure) : ProbeDbCommand
    {
        public override int CommandTimeout { get => 17; set => throw failure; }
    }

    private static Task<Option<DatabaseDefinition, IDLOptionFailure>> ImportMetadata(IMetadataFromSqlFactory factory, CancellationToken token = default) =>
        factory.ParseDatabaseAsyncCore("metadata", "MetadataDb", "Tests", "destination", "captured connection", token);

    private static Exception MetadataException(Option<DatabaseDefinition, IDLOptionFailure> result)
    {
        if (result.TryUnwrap(out _, out var failure)) throw new InvalidOperationException("Expected failed metadata Option.");
        return failure.FailureValue as Exception ?? throw new InvalidOperationException("Expected original operational exception.");
    }

    private class LegacyMetadataTestFactory : IMetadataFromSqlFactory
    {
        internal int SyncReads { get; private set; }
        public Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString)
        { SyncReads++; throw new InvalidOperationException("Synchronous metadata fallback."); }
    }

    private sealed class MetadataTestFactory(Func<MetadataHarness> create) : LegacyMetadataTestFactory, IAsyncMetadataFactory
    {
        public MetadataFromDatabaseFactoryOptions Options { get; set; }
        internal MetadataImportRequest? Request { get; private set; }
        public IAsyncMetadataReadPlan CaptureImport(MetadataImportRequest request)
        {
            Request = request;
            return new MetadataPlan(create(), request.Settings, request.DatabaseName);
        }
    }

    private sealed class MetadataTestProvider(ScriptedMutationScenario scenario, Func<MetadataHarness> create)
        : ScriptedMutationProvider<TransactionMutationGuardDb>(scenario), IAsyncProviderMetadataSource
    {
        internal int Captures { get; private set; }
        internal void SetEffectiveName(string name) => DatabaseName = name;
        public IAsyncMetadataReadPlan CaptureValidationMetadata(MetadataReadSettings settings)
        {
            Captures++;
            return new MetadataPlan(create(), settings, DatabaseName);
        }
    }

    private sealed class MetadataPlan : IAsyncMetadataReadPlan
    {
        private readonly MetadataHarness harness;
        internal MetadataPlan(MetadataHarness harness, MetadataReadSettings settings, string identity)
        { this.harness = harness; harness.Settings = settings; harness.Identity = identity; }
        public void Validate() => harness.Validate(harness.Settings!.CommandTimeoutSeconds);
        public IAsyncMetadataSession CreateSession() => harness.Creating is { } create ? create() : harness.Session;
        public Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadAsync(MetadataReadContext context, CancellationToken token)
        {
            harness.Context = context;
            return harness.Parser?.Invoke(context, token) ?? harness.ReadDefault(context, token);
        }
    }

    private sealed class MetadataHarness : IAsyncMetadataCommands
    {
        internal MetadataHarness(int[]? tables = null)
        {
            var ids = tables ?? [11, 22];
            Queries.Add("tables", new(ids));
            foreach (var id in ids)
            {
                Queries.Add("columns:" + id, new([id * 10]));
                Queries.Add("ddl:" + id, new([], scalar: true));
            }
            Session = new(this);
        }
        internal Dictionary<string, MetadataQuery> Queries { get; } = [];
        internal List<string> Executed { get; } = [];
        internal MetadataSession Session { get; }
        internal Func<IAsyncMetadataSession>? Creating { get; set; }
        internal MetadataReadSettings? Settings { get; set; }
        internal string Identity { get; set; } = "destination";
        internal bool SupportsTimeout { get; set; } = true;
        internal Action? Materializing { get; set; }
        internal MetadataReadContext? Context { get; set; }
        internal Func<MetadataReadContext, CancellationToken, Task<Option<DatabaseDefinition, IDLOptionFailure>>>? Parser { get; set; }
        internal Func<CapturedSql, IAsyncOwnedCommandFactory>? CaptureOverride { get; set; }
        public void Validate(int? timeout)
        { if (timeout is not null && !SupportsTimeout) throw new NotSupportedException("Explicit metadata timeout is unsupported."); }
        public IAsyncOwnedCommandFactory Capture(CapturedSql query)
        {
            if (CaptureOverride is not null) return CaptureOverride(query);
            var sql = query.ToSql();
            var command = Queries[sql.Text].Command;
            command.Creating = () => { command.Resource.Borrowed.CommandText = sql.Text; return command.Resource; };
            return command;
        }
        internal async Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadDefault(MetadataReadContext context, CancellationToken token)
        {
            Settings!.Log?.Invoke("Reading metadata");
            var tables = await context.ReadAsync(new Sql("tables"), reader => { Materializing?.Invoke(); return reader.GetInt32(0); });
            var selected = tables.Where(id => Settings.Include.Count == 0 || Settings.Include.Contains("table_" + id)).ToArray();
            if (Settings.Purpose == MetadataReadPurpose.Import && (selected.Length == 0 || Settings.Include.Any(name => !selected.Any(id => name == "table_" + id))))
                return DLOptionFailure.Fail(DLFailureType.InvalidModel, "Missing selected objects or empty import schema.");
            var models = new List<MetadataTableModelDraft>();
            foreach (var id in selected)
            {
                token.ThrowIfCancellationRequested();
                var columns = await context.ReadAsync(new Sql("columns:" + id), reader => reader.GetInt32(0));
                if (await context.ExecuteScalarAsync(new Sql("ddl:" + id)) is not string)
                    return DLOptionFailure.Fail(DLFailureType.InvalidModel, "Missing table definition SQL.");
                models.Add(new("Items" + id, new MetadataModelDraft(new("Item" + id, "Tests", ModelCsType.Class))
                {
                    ValueProperties = columns.Select(column => new MetadataValuePropertyDraft("Value" + column,
                        new(typeof(int)), new("value_" + column) { PrimaryKey = true })
                    { Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("value_" + column)] }).ToArray()
                }, new("table_" + id)));
            }
            return Build(models);
        }
        internal Option<DatabaseDefinition, IDLOptionFailure> EmptyDefinition() => Build([]);
        private Option<DatabaseDefinition, IDLOptionFailure> Build(IReadOnlyList<MetadataTableModelDraft> models) =>
            new MetadataDefinitionFactory().BuildProviderMetadata(new MetadataDatabaseDraft("metadata", new("MetadataDb", "Tests", ModelCsType.Class))
            { DbName = Identity, TableModels = models });
    }

    private sealed class MetadataQuery
    {
        internal MetadataQuery(int[] rows, bool scalar = false)
        {
            Scalar = scalar;
            Reader = new(rows);
            Access = new() { Reader = Reader, ScalarResult = "CREATE TABLE ..." };
            Command.Resource.Borrowed.CommandTimeout = 17;
        }
        internal ControlledAsyncDataReader Reader { get; }
        internal bool Scalar { get; }
        internal ControlledAsyncDatabaseAccess Access { get; set; }
        internal ControlledOwnedCommandFactory Command { get; } = new();
    }

    private sealed class MetadataSession(MetadataHarness harness) : IAsyncMetadataSession
    {
        public IAsyncDatabaseAccess Access { get; set; } = new MetadataAccess(harness);
        public IAsyncMetadataCommands Commands { get; set; } = harness;
        internal AsyncCheckpoint Opening { get; set; } = new();
        internal AsyncCheckpoint Cleanup { get; set; } = new();
        internal int Opens { get; private set; }
        internal int Disposals { get; private set; }
        internal bool IgnoreOpenCancellation { get; set; }
        internal CancellationToken OpenToken { get; private set; }
        public async Task OpenAsync(CancellationToken token)
        { Opens++; OpenToken = token; await Opening.ReachAsync(IgnoreOpenCancellation ? CancellationToken.None : token); }
        public async ValueTask DisposeAsync() { Disposals++; await Cleanup.ReachAsync(CancellationToken.None); }
    }

    private sealed class MetadataAccess(MetadataHarness harness) : AsyncDatabaseAccess
    {
        protected override void ValidateCommand(IDbCommand command, AsyncCommandKind kind)
        { if (command is not ControlledCommand || kind is not (AsyncCommandKind.Reader or AsyncCommandKind.Scalar)) throw new NotSupportedException(); }
        protected override Task<IAsyncDataReader> ExecuteReaderCoreAsync(IDbCommand command, CancellationToken token)
            => Dispatch(command).ExecuteReaderAsync(command, token);
        protected override Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken token)
            => Dispatch(command).ExecuteScalarAsync(command, token);
        private ControlledAsyncDatabaseAccess Dispatch(IDbCommand command)
        {
            foreach (var prior in harness.Executed)
            {
                var query = harness.Queries[prior];
                if ((!query.Scalar && !query.Reader.IsDisposed) || query.Command.Resource.AsyncDisposals != 1)
                    throw new InvalidOperationException("A previous metadata command has not finished cleanup.");
            }
            harness.Executed.Add(command.CommandText);
            return harness.Queries[command.CommandText].Access;
        }
        protected override Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken token) => throw new NotSupportedException();
    }
}
