using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("availability", false)]
    [Arguments("availability", true)]
    [Arguments("database", false)]
    [Arguments("database", true)]
    [Arguments("table", false)]
    [Arguments("table", true)]
    public async Task AdministrativeDiagnostics_ProbeInterpretationIsMaterializationNotNativeTimeout(string kind, bool timeoutException)
    {
        Exception expected = timeoutException ? new TimeoutException("local interpretation") : new Exception("local interpretation");
        var cleanup = new Exception("session cleanup");
        var harness = new ExistenceProbeHarness { Interpret = _ => throw expected, Classify = _ => true };
        harness.Session.Cleanup = ProbeFault(cleanup);
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        Exception? failure = null;
        try { await RunProbe(provider, kind).WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception observed) when (ReferenceEquals(observed, expected)) { failure = observed; }
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure!)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(harness.Classifications).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AdministrativeDiagnostics_InvalidMetadataKeepsTheFailingBoundary(bool import, bool nullSuccess)
    {
        var harness = new MetadataHarness();
        Exception? parserFailure = null;
        harness.Parser = (_, _) =>
        {
            Option<DatabaseDefinition, IDLOptionFailure> missing = default;
            // The Option library rejects null at construction, inside the parser.
            // Only a default Option reaches the coordinator's complete-result guard.
            try { if (nullSuccess) missing = (DatabaseDefinition)null!; }
            catch (Exception failure) { parserFailure = failure; throw; }
            return Task.FromResult(missing);
        };
        using var provider = new MetadataTestProvider(new(), () => harness);
        var result = await (import ? ImportMetadata(new MetadataTestFactory(() => harness)) : provider.ReadValidationMetadataAsyncCore());
        var failure = MetadataException(result);
        if (nullSuccess) await Assert.That(failure).IsSameReferenceAs(parserFailure);
        else await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(nullSuccess ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.InvalidOperation);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(import ? null : provider.TelemetryInstanceId);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministrativeDiagnostics_MetadataLoggerIsNotificationAndKeepsCleanup(bool import)
    {
        var harness = new MetadataHarness();
        var primary = new TimeoutException("logger");
        var cleanup = new Exception("session cleanup");
        harness.Session.Cleanup = ProbeFault(cleanup);
        Action<string> log = _ => throw primary;
        using var provider = new MetadataTestProvider(new(), () => harness);
        var factory = new MetadataTestFactory(() => harness) { Options = new() { Log = log } };
        var result = await (import ? ImportMetadata(factory) : provider.ReadValidationMetadataAsyncCore(log: log));
        var failure = MetadataException(result);
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(import ? null : provider.TelemetryInstanceId);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(harness.Executed).IsEmpty();
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AdministrativeDiagnostics_LocalParserFailureIsMaterialization(bool import, bool timeoutException)
    {
        Exception expected = timeoutException ? new TimeoutException("local parser") : new Exception("local parser");
        var harness = new MetadataHarness { Parser = (_, _) => throw expected };
        using var provider = new MetadataTestProvider(new(), () => harness);
        var result = await (import ? ImportMetadata(new MetadataTestFactory(() => harness)) : provider.ReadValidationMetadataAsyncCore());
        var failure = MetadataException(result);
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(import ? null : provider.TelemetryInstanceId);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AdministrativeDiagnostics_LocalCancellationKeepsRequestAttribution(bool metadata, bool requestCancellation)
    {
        using var request = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        var expected = new OperationCanceledException(requestCancellation ? request.Token : foreign.Token);
        void Fail()
        {
            if (requestCancellation) request.Cancel(); else foreign.Cancel();
            throw expected;
        }
        var probe = new ExistenceProbeHarness { Interpret = _ => { Fail(); return false; } };
        var reader = new MetadataHarness { Parser = (_, _) => { Fail(); return Task.FromResult(default(Option<DatabaseDefinition, IDLOptionFailure>)); } };
        using var probeProvider = new ExistenceProbeProvider(new(), () => probe);
        using var metadataProvider = new MetadataTestProvider(new(), () => reader);
        var failure = await AsyncEnumerationFailureOf(() => metadata
            ? (Task)metadataProvider.ReadValidationMetadataAsyncCore(token: request.Token)
            : probeProvider.DatabaseExistsAsyncCore(token: request.Token));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(expected.CancellationToken).IsEqualTo(requestCancellation ? request.Token : foreign.Token);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(requestCancellation ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(metadata ? ExecutionOperationKind.MetadataRead : ExecutionOperationKind.ExistenceCheck);
        await Assert.That(metadata ? reader.Session.Disposals : probe.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AdministrativeDiagnostics_LoggerUsesOnlyCurrentNestedReports(bool import, bool currentReport)
    {
        var expected = new Exception("logger occurrence");
        ExecutionFailureContext Report() => new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand);
        var previous = Report();
        ExecutionFailureContexts.Attach(expected, previous);
        Action<string> log = _ =>
        {
            if (currentReport) ExecutionFailureContexts.Attach(expected, Report());
            throw expected;
        };
        var harness = new MetadataHarness();
        using var provider = new MetadataTestProvider(new(), () => harness);
        var factory = new MetadataTestFactory(() => harness) { Options = new() { Log = log } };
        var failure = MetadataException(await (import ? ImportMetadata(factory) : provider.ReadValidationMetadataAsyncCore(log: log)));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(currentReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(currentReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Notification);
        await Assert.That(context.Operation).IsEqualTo(currentReport ? ExecutionOperationKind.RawCommand : ExecutionOperationKind.MetadataRead);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(import ? null : provider.TelemetryInstanceId);
        await Assert.That(previous.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(previous.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministrativeDiagnostics_RepeatedLoggerFailureDoesNotBorrowTheFirstOccurrence(bool swallowLast)
    {
        var expected = new Exception("reused logger exception");
        var calls = 0;
        ExecutionFailureContext? previous = null;
        var harness = new MetadataHarness();
        harness.Parser = (_, _) =>
        {
            try { harness.Settings!.Log!("first"); } catch { previous = ExecutionFailureContexts.Get(expected); }
            try { harness.Settings!.Log!("second"); } catch when (swallowLast) { }
            return Task.FromResult(harness.EmptyDefinition());
        };
        var factory = new MetadataTestFactory(() => harness)
        {
            Options = new()
            {
                Log = _ =>
                {
                    if (++calls == 1)
                        ExecutionFailureContexts.Attach(expected, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand));
                    throw expected;
                }
            }
        };
        var result = await ImportMetadata(factory);
        if (swallowLast) await Assert.That(result.TryUnwrap(out _, out _)).IsTrue();
        else await Assert.That(MetadataException(result)).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(previous!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }
}
