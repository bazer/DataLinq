using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class AsyncProvisioningTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministrativeOccurrences_MissingProvisioningSessionIsKnownButProviderFailureIsNot(bool missingSession)
    {
        var factory = new ProvisioningFactory();
        var expected = new InvalidOperationException("provider construction failed");
        factory.Plan.Creating = () => missingSession ? null! : throw expected;
        var failure = await Fails(() => Create(factory));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        if (!missingSession) await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(missingSession ? ExecutionFailureCause.InvalidOperation : ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Provisioning);
        await Assert.That(context.ProviderInstanceId).IsNull();
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(factory.Session.Initializes).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(0);
        await Assert.That(factory.Session.CreatedDestination).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AdministrativeOccurrences_ProvisioningCleanupCannotBorrowAnEarlierFailure(bool workFails, bool freshReport)
    {
        var occurrence = new AdministrativeFailureOccurrence();
        var factory = new ProvisioningFactory();
        factory.Owned.Resource.Disposing = occurrence.RecordEarlierFailure;
        factory.Session.Cleanup = occurrence.Cleanup(freshReport, deferred: workFails);
        if (workFails)
        {
            var dispatch = new AsyncCheckpoint(paused: true);
            dispatch.Fail(occurrence.WorkFailure);
            factory.Session.Access = new ControlledAsyncDatabaseAccess(dispatch);
        }
        var pending = Fails(() => Create(factory));
        if (workFails)
        {
            try
            {
                await factory.Session.Cleanup.Entered.WaitAsync(Timeout);
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(factory.Session.ResourcesLive).IsTrue();
                await Assert.That(factory.Owned.Resource.AsyncDisposals).IsEqualTo(1);
            }
            finally { factory.Session.Cleanup.Fail(occurrence.Reused); }
        }
        var failure = await pending;
        await occurrence.AssertResult(failure, workFails, freshReport, ExecutionOperationKind.Provisioning, null);
        await Assert.That(factory.Owned.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
        await Assert.That(factory.Session.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        await Assert.That(factory.Session.ResourcesLive).IsFalse();
        await Assert.That(factory.Session.CreatedDestination).IsTrue();
    }
}
