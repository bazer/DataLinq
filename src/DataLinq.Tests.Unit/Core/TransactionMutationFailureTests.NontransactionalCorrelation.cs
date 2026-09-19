using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.SQLite;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("local")]
    [Arguments("scalar")]
    [Arguments("reader")]
    public async Task NontransactionalCorrelation_ProbeKeepsProviderAndPurposeThroughCleanup(string shape)
    {
        var primary = new Exception("probe execution");
        var cleanup = new Exception("probe session cleanup");
        var harness = new ExistenceProbeHarness { Shape = shape, LocalRead = () => throw primary, Interpret = _ => throw primary };
        if (shape == "reader") harness.Reader.Advance = JournalFault(primary);
        if (shape != "local") harness.Session.Cleanup = JournalFault(cleanup);
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.DatabaseExistsAsyncCore());
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        if (shape != "local")
        {
            await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
            await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task NontransactionalCorrelation_MetadataImportAndRuntimeKeepActualProviderBoundary(bool import, bool scalar)
    {
        var primary = new Exception("metadata command");
        var cleanup = new Exception("metadata session cleanup");
        var harness = new MetadataHarness();
        if (scalar) harness.Queries["ddl:11"].Access = new(JournalFault(primary));
        else harness.Queries["tables"].Reader.Advance = JournalFault(primary);
        harness.Session.Cleanup = JournalFault(cleanup);
        using var provider = new MetadataTestProvider(new(), () => harness);
        var result = await (import ? ImportMetadata(new MetadataTestFactory(() => harness)) : provider.ReadValidationMetadataAsyncCore());
        var failure = MetadataException(result);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(import ? null : provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
    }

    [Test]
    public async Task NontransactionalCorrelation_MetadataImportDropsForeignProviderIdentity()
    {
        var primary = new Exception("reused provider exception");
        ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand,
            providerInstanceId: "previous-provider"));
        var harness = new MetadataHarness();
        harness.Queries["tables"].Reader.Advance = JournalFault(primary);
        var failure = MetadataException(await ImportMetadata(new MetadataTestFactory(() => harness)));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(context.ProviderInstanceId).IsNull();
        await Assert.That(context.TransactionId).IsNull();
    }

    [Test]
    public async Task NontransactionalCorrelation_ConfigurationCapturesProviderThroughOwnedCommandCleanup()
    {
        var primary = new Exception("journal mode command");
        var cleanup = new Exception("command cleanup");
        var harness = new JournalModeHarness();
        harness.Session.Access = new ControlledAsyncDatabaseAccess(JournalFault(primary));
        harness.Commands.Resource.Cleanup = JournalFault(cleanup);
        using var provider = new JournalModeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.ProviderConfiguration);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NontransactionalCorrelation_CacheDisposalReportsItsCapturedProvider(bool asynchronous)
    {
        using var provider = new ScriptedMutationProvider(new());
        provider.State.Cache.Dispose();
        var cache = provider.State.Cache = new DatabaseCache(provider, DataLinqLoggingConfiguration.NullConfiguration, _ => null);
        var primary = new Exception("cache notification cleanup");
        var notification = new ThrowingNotification(primary);
        cache.TableCaches.Values.First().SubscribeToChanges(notification);
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => cache.DisposeAsyncCore().AsTask()) : Capture<Exception>(cache.Dispose);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        GC.KeepAlive(notification);
    }
}
