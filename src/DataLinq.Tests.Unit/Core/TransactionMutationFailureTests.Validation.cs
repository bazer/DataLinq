using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Metadata;
using DataLinq.Validation;
using DataLinq.Tests.Unit.Fixtures;
using ThrowAway;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task PublicValidation_CancellationWaitsForOwnedCleanupAndPreservesToken()
    {
        var harness = new MetadataHarness([]);
        harness.Session.Cleanup = new AsyncCheckpoint(paused: true);
        using var provider = new MetadataTestProvider(new(), () => harness);
        harness.Parser = (_, _) =>
        {
            harness.Settings!.SetIdentifiers(SchemaIdentifierRules.SQLite);
            return Task.FromResult<Option<DatabaseDefinition, IDLOptionFailure>>(provider.Metadata);
        };
        using var cancellation = new CancellationTokenSource();
        var pending = DataLinqSchemaValidator.ValidateAsync(provider, cancellationToken: cancellation.Token);
        await harness.Session.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(harness.Session.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        harness.Session.Cleanup.Release();
        var failure = await Assert.That(async () => { await pending; }).Throws<OperationCanceledException>();
        await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task PublicValidation_AsyncCapturePrecedesSuspensionAndPublicationWaitsForCleanup()
    {
        var harness = new MetadataHarness([]);
        harness.Session.Opening = new(paused: true);
        harness.Session.Cleanup = new(paused: true);
        using var provider = new MetadataTestProvider(new(), () => harness);
        var name = provider.Metadata.TableModels[0].Table.DbName;
        var include = new List<string> { name };
        var logs = 0;
        var options = new DataLinqSchemaValidationOptions
        {
            Include = include, CommandTimeout = TimeSpan.FromMilliseconds(1001), TreatValidationIssuesAsFailures = false,
            MetadataReaderLog = _ => logs++
        };
        harness.Parser = (_, _) =>
        {
            harness.Settings!.SetIdentifiers(SchemaIdentifierRules.SQLite);
            harness.Settings.Limitations!.Add(new(name, "unrepresented", SchemaReadAspect.Index,
                SchemaReadLimitationReason.PartialIndex, "original typed diagnostic"));
            harness.Settings.Log?.Invoke("original callback diagnostic");
            return Task.FromResult<Option<DatabaseDefinition, IDLOptionFailure>>(provider.Metadata);
        };
        var pending = DataLinqSchemaValidator.ValidateAsync(provider, options);
        await harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        include[0] = "invalid-later-name";
        options.Include = [];
        options.TreatValidationIssuesAsFailures = true;
        options.CommandTimeout = TimeSpan.FromSeconds(-1);
        options.MetadataReaderLog = _ => throw new InvalidOperationException("late callback must not run");
        harness.Session.Opening.Release();
        await harness.Session.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        harness.Session.Cleanup.Release();
        var result = await pending;
        await Assert.That(result.ModelTableCount).IsEqualTo(1);
        await Assert.That(result.DatabaseTableCount).IsEqualTo(1);
        await Assert.That(result.HasFailures).IsFalse();
        await Assert.That(result.Issues.Single().Message).IsEqualTo("original typed diagnostic");
        await Assert.That(logs).IsEqualTo(1);
        await Assert.That(harness.Settings!.CommandTimeoutSeconds).IsEqualTo(2);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task PublicValidation_FailedReadAndCleanupRetainBothWithoutResult()
    {
        var failure = DLOptionFailure.Fail(DLFailureType.InvalidModel, "original incomplete metadata");
        var cleanup = new InvalidOperationException("original cleanup");
        var sync = new SyncMetadataHarness { Parse = _ => failure, SessionCleanupFailure = cleanup };
        using var syncProvider = new SyncMetadataProvider(sync);
        var observed = await Assert.That(() => DataLinqSchemaValidator.Validate(syncProvider)).Throws<DataLinqSchemaReadException>();
        await Assert.That(observed!.Failure.InnerFailures[0]).IsSameReferenceAs(failure);
        await Assert.That(observed.Exceptions.Single()).IsSameReferenceAs(cleanup);
        await Assert.That(sync.Disposals).IsEqualTo(1);

        var asyncHarness = new MetadataHarness([]);
        asyncHarness.Parser = (_, _) => Task.FromResult<Option<DatabaseDefinition, IDLOptionFailure>>(failure);
        using var asyncProvider = new MetadataTestProvider(new(), () => asyncHarness);
        var asyncObserved = await Assert.That(async () => { await DataLinqSchemaValidator.ValidateAsync(asyncProvider); }).Throws<DataLinqSchemaReadException>();
        await Assert.That(asyncObserved!.Failure).IsSameReferenceAs(failure);
        await Assert.That(asyncHarness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task PublicValidation_UnsupportedProviderDoesNotCreateReadOrTransactionWork()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new ScriptedMutationProvider(scenario);
        await Assert.That(() => DataLinqSchemaValidator.Validate(provider)).Throws<NotSupportedException>();
        await Assert.That(() => { _ = DataLinqSchemaValidator.ValidateAsync(provider, cancellationToken: new(true)); }).Throws<NotSupportedException>();
        await Assert.That(scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(scenario.CreatedTransactionTypes).IsEmpty();
    }
}
