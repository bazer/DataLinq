using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.SQLite;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("access", false)]
    [Arguments("access", true)]
    [Arguments("open", false)]
    [Arguments("open", true)]
    [Arguments("command-validation", false)]
    [Arguments("command-validation", true)]
    public async Task AdministrativeLifetimeReview_JournalUsesOnlyTheFailingPhaseReport(string phase, bool freshReport)
    {
        var occurrence = new AdministrativeSetupOccurrence(phase, freshReport);
        var harness = new JournalModeHarness { Creating = () => occurrence.Capture() };
        using var provider = new JournalModeProvider(new(), () => harness);
        var failure = await AsyncEnumerationFailureOf(() => provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL));
        await occurrence.AssertFailure(failure, ExecutionOperationKind.ProviderConfiguration, provider.TelemetryInstanceId);
    }
}

public sealed partial class AsyncProvisioningTests
{
    [Test]
    [Arguments("access", false)]
    [Arguments("access", true)]
    [Arguments("open", false)]
    [Arguments("open", true)]
    [Arguments("command-validation", false)]
    [Arguments("command-validation", true)]
    public async Task AdministrativeLifetimeReview_ProvisioningUsesOnlyTheFailingPhaseReport(string phase, bool freshReport)
    {
        var occurrence = new AdministrativeSetupOccurrence(phase, freshReport);
        var factory = new ProvisioningFactory();
        factory.Plan.Creating = () => occurrence.Capture();
        var failure = await Fails(() => Create(factory));
        await occurrence.AssertFailure(failure, ExecutionOperationKind.Provisioning, null);
    }
}

internal sealed class AdministrativeSetupOccurrence(string phase, bool freshReport)
    : IAsyncJournalModeSession, IAsyncProvisioningSession, IAsyncOwnedCommandFactory
{
    private readonly Exception reused = new("later administrative phase");
    private readonly Exception oldCleanup = new("earlier unrelated cleanup");
    private readonly ControlledAsyncDatabaseAccess access = new();
    private ExecutionFailureContext? earlier;
    private bool opened;
    private int opens;
    private int validations;
    private int creations;
    private int disposals;

    internal AdministrativeSetupOccurrence Capture()
    {
        if (phase != "command-validation") ReportEarlier();
        return this;
    }

    public IAsyncDatabaseAccess Access
    {
        get { if (phase == "access") FailCurrent(); return access; }
    }
    public IAsyncOwnedCommandFactory CommandFactory => this;

    public async Task OpenAsync(CancellationToken token)
    {
        opens++;
        await Task.Yield();
        if (phase == "open") FailCurrent();
        ReportEarlier();
        opened = true;
    }

    public Task InitializeAsync(CancellationToken token) => OpenAsync(token);
    public ValueTask DisposeAsync() { disposals++; return default; }

    public void Validate(AsyncCommandKind kind)
    {
        validations++;
        if (opened) FailCurrent();
    }

    public IAsyncOwnedCommand Create()
    {
        creations++;
        throw new InvalidOperationException("No command should be created after the earlier failure.");
    }

    private void ReportEarlier()
    {
        using var nested = ExecutionFailureScope.Begin();
        earlier = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
            [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, oldCleanup, ExecutionOperationKind.Dispose)],
            operation: ExecutionOperationKind.RawCommand);
        ExecutionFailureContexts.Attach(reused, earlier);
    }

    private void FailCurrent()
    {
        if (freshReport)
            ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.RowLoading,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand));
        throw reused;
    }

    internal async Task AssertFailure(Exception failure, ExecutionOperationKind operation, string? providerId)
    {
        await Assert.That(failure).IsSameReferenceAs(reused);
        var report = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(report.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        var fallbackStage = phase switch
        {
            "access" => ExecutionFailureStage.Validation,
            "open" => ExecutionFailureStage.Initialization,
            _ => ExecutionFailureStage.CommandExecution
        };
        await Assert.That(report.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.RowLoading : fallbackStage);
        await Assert.That(report.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.RawCommand : operation);
        await Assert.That(report.ProviderInstanceId).IsEqualTo(providerId);
        await Assert.That(report.TransactionId).IsNull();
        await Assert.That(report.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(report.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(report.SecondaryFailures).IsEmpty();
        await Assert.That(report.HasCleanupFailure).IsFalse();
        await Assert.That(earlier!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(earlier.SecondaryFailures[0].Exception).IsSameReferenceAs(oldCleanup);
        await Assert.That(disposals).IsEqualTo(1);
        await Assert.That(opens).IsEqualTo(phase == "access" ? 0 : 1);
        await Assert.That(validations).IsEqualTo(phase == "access" ? 0 : phase == "open" ? 1 : 2);
        await Assert.That(creations).IsEqualTo(0);
        await Assert.That(access.Calls).IsEmpty();
    }
}
