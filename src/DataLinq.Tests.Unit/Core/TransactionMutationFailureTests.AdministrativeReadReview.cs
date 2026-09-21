using System;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministrativeReadReview_PreDispatchFailureCannotBecomeUnavailable(bool sessionCreation)
    {
        var expected = new Exception("construction failed after nested command");
        var harness = new ExistenceProbeHarness { Classify = _ => true };
        void Fail()
        {
            ExecutionFailureContexts.Attach(expected, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand));
            throw expected;
        }
        if (sessionCreation) harness.Creating = () => { Fail(); return harness.Session; };
        else harness.Commands.Creating = () => { Fail(); return harness.Commands.Resource; };
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.FileOrServerExistsAsyncCore());
        await Assert.That(failure).IsSameReferenceAs(expected);
        var report = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(report.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(report.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(report.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(report.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(report.SecondaryFailures).IsEmpty();
        await Assert.That(harness.Classifications).IsEqualTo(0);
        await Assert.That(harness.Session.Disposals).IsEqualTo(sessionCreation ? 0 : 1);
        await Assert.That(harness.Session.Opens).IsEqualTo(sessionCreation ? 0 : 1);
        await Assert.That(harness.Commands.Resource.AsyncDisposals).IsEqualTo(0);
    }

    [Test]
    [Arguments("metadata", false, false)]
    [Arguments("metadata", false, true)]
    [Arguments("metadata", true, false)]
    [Arguments("metadata", true, true)]
    [Arguments("import", false, false)]
    [Arguments("import", false, true)]
    [Arguments("import", true, false)]
    [Arguments("import", true, true)]
    [Arguments("availability", false, false)]
    [Arguments("availability", false, true)]
    [Arguments("availability", true, false)]
    [Arguments("availability", true, true)]
    [Arguments("database", false, false)]
    [Arguments("database", false, true)]
    [Arguments("database", true, false)]
    [Arguments("database", true, true)]
    [Arguments("table", false, false)]
    [Arguments("table", false, true)]
    [Arguments("table", true, false)]
    [Arguments("table", true, true)]
    public Task AdministrativeReadReview_SettledWorkCannotClassifyLaterLocalFailure(string kind, bool freshReport, bool fromOpening) =>
        AssertSettledAdministrativeWork(kind, freshReport, fromOpening, reader: false);

    [Test]
    [Arguments("metadata", false)]
    [Arguments("metadata", true)]
    [Arguments("import", false)]
    [Arguments("import", true)]
    public Task AdministrativeReadReview_SettledReaderCannotClassifyLaterParserFailure(string kind, bool freshReport) =>
        AssertSettledAdministrativeWork(kind, freshReport, fromOpening: false, reader: true);

    private static async Task AssertSettledAdministrativeWork(string kind, bool freshReport, bool fromOpening, bool reader)
    {
        var reused = new Exception("later local failure");
        var oldSecondary = new Exception("earlier secondary");
        ExecutionFailureContext? previous = null;
        void ReportEarlier()
        {
            using var nested = ExecutionFailureScope.Begin();
            previous = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
                [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, oldSecondary, ExecutionOperationKind.Dispose)],
                operation: ExecutionOperationKind.RawCommand);
            ExecutionFailureContexts.Attach(reused, previous);
        }
        void FailLocally()
        {
            if (freshReport)
                ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                    ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand));
            throw reused;
        }
        var metadata = new MetadataHarness();
        var probe = new ExistenceProbeHarness
        {
            Interpret = _ => { FailLocally(); return false; },
            Classify = _ => true
        };
        var query = reader ? "tables" : "ddl:11";
        var command = kind is "metadata" or "import" ? metadata.Queries[query].Command.Resource : probe.Commands.Resource;
        if (fromOpening)
        {
            metadata.Session.OpeningCompleted = ReportEarlier;
            probe.Session.OpeningCompleted = ReportEarlier;
        }
        else command.Disposing = ReportEarlier;
        metadata.Parser = async (context, _) =>
        {
            if (reader) await context.ReadAsync(new Sql(query), row => row.GetInt32(0));
            else if (!fromOpening) await context.ExecuteScalarAsync(new Sql(query));
            FailLocally();
            return metadata.EmptyDefinition();
        };
        using var metadataProvider = new MetadataTestProvider(new(), () => metadata);
        using var probeProvider = new ExistenceProbeProvider(new(), () => probe);
        var failure = kind switch
        {
            "metadata" => MetadataException(await metadataProvider.ReadValidationMetadataAsyncCore()),
            "import" => MetadataException(await ImportMetadata(new MetadataTestFactory(() => metadata))),
            _ => await AsyncEnumerationFailureOf(() => RunProbe(probeProvider, kind))
        };
        await Assert.That(failure).IsSameReferenceAs(reused);
        var report = ExecutionFailureContexts.Get(failure)!;
        var operation = kind is "metadata" or "import" ? ExecutionOperationKind.MetadataRead : ExecutionOperationKind.ExistenceCheck;
        await Assert.That(report.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.MaterializationError);
        await Assert.That(report.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Materialization);
        await Assert.That(report.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.RawCommand : operation);
        await Assert.That(report.ProviderInstanceId).IsEqualTo(kind == "import" ? null : kind == "metadata" ? metadataProvider.TelemetryInstanceId : probeProvider.TelemetryInstanceId);
        await Assert.That(report.SecondaryFailures).IsEmpty();
        await Assert.That(report.HasCleanupFailure).IsFalse();
        await Assert.That(report.TransactionId).IsNull();
        await Assert.That(report.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(report.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(previous!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(previous.SecondaryFailures[0].Exception).IsSameReferenceAs(oldSecondary);
        await Assert.That(command.AsyncDisposals).IsEqualTo(fromOpening && (kind is "metadata" or "import") ? 0 : 1);
        await Assert.That(command.SyncDisposals).IsEqualTo(0);
        if (reader) await Assert.That(metadata.Queries[query].Reader.IsDisposed).IsTrue();
        await Assert.That(kind is "metadata" or "import" ? metadata.Session.Disposals : probe.Session.Disposals).IsEqualTo(1);
        await Assert.That(probe.Classifications).IsEqualTo(0);
    }
}
