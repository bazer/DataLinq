using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed class PublicFailureDiagnosticsTests
{
    [Test]
    public async Task AccessorInspectsOnlySuppliedExceptionAndProtectsPreviousSnapshots()
    {
        var original = new Exception("original");
        var cleanup = new Exception("cleanup");
        var source = new ExecutionFailureContext(ExecutionFailureCause.ApplicationError, ExecutionFailureStage.Callback,
            ExecutionCompletion.RolledBack, ExecutionRecoveryActions.Dispose, 17,
            [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, cleanup, ExecutionOperationKind.Dispose)],
            operation: ExecutionOperationKind.TransactionCallback, providerInstanceId: "provider");
        ExecutionFailureContexts.Attach(original, source);
        var snapshot = DataLinqFailure.GetContext(original)!;
        await Assert.That(snapshot.Cause).IsEqualTo(DataLinqFailureCause.ApplicationError);
        await Assert.That(snapshot.Stage).IsEqualTo(DataLinqFailureStage.Callback);
        await Assert.That(snapshot.Operation).IsEqualTo(DataLinqOperationKind.TransactionCallback);
        await Assert.That(snapshot.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.RolledBack);
        await Assert.That(snapshot.TransactionId).IsEqualTo(17u);
        await Assert.That(snapshot.ProviderInstanceId).IsEqualTo("provider");
        await Assert.That(snapshot.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(DataLinqFailure.GetContext(original)).IsSameReferenceAs(snapshot);
        await Assert.That(DataLinqFailure.GetContext(new Exception("wrapper", original))).IsNull();
        await Assert.That(DataLinqFailure.GetContext(new AggregateException(original))).IsNull();
        await Assert.That(DataLinqFailure.GetContext(cleanup)).IsNull();
        await Assert.That(() => DataLinqFailure.GetContext(null!)).Throws<ArgumentNullException>();
        await Assert.That(snapshot.SecondaryFailures is DataLinqSecondaryFailure[]).IsFalse();
        var list = (IList<DataLinqSecondaryFailure>)snapshot.SecondaryFailures;
        await Assert.That(() => list.Clear()).Throws<NotSupportedException>();
        await Assert.That(() => list[0] = list[0]).Throws<NotSupportedException>();
        ExecutionFailureContexts.Attach(original, source.AfterRecovery(ExecutionCompletion.RolledBack, ExecutionRecoveryActions.None));
        await Assert.That(DataLinqFailure.GetContext(original)!.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.None);
        await Assert.That(snapshot.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.Dispose);
    }

    [Test]
    public async Task PublicEnumsHaveTheAcceptedIndependentNumericAssignments()
    {
        await Assert.That(Values<DataLinqFailureCause>()).IsEqualTo("Unknown=0,Cancellation=1,Timeout=2,ProviderError=3,ApplicationError=4,MaterializationError=5,LocalFinalizationError=6,InvalidOperation=7");
        await Assert.That(Values<DataLinqOperationKind>()).IsEqualTo("Unknown=0,Query=1,KeyLookup=2,RelationLoad=3,Insert=4,Update=5,Save=6,Delete=7,Commit=8,Rollback=9,Dispose=10,TransactionCallback=11,RawCommand=12,MetadataRead=13,SchemaValidation=14,ExistenceCheck=15,Provisioning=16,ProviderConfiguration=17");
        await Assert.That(Values<DataLinqFailureStage>()).IsEqualTo("Unknown=0,Validation=1,Initialization=2,CommandExecution=3,RowLoading=4,Callback=5,LocalFinalization=6,Commit=7,Rollback=8,CacheRecovery=9,Notification=10,Cleanup=11");
        await Assert.That(Values<DataLinqCompletionOutcome>()).IsEqualTo("Unknown=0,NotApplicable=1,NotAttempted=2,Committed=3,RolledBack=4");
        await Assert.That(Values<DataLinqRecoveryActions>()).IsEqualTo("None=0,Continue=1,Rollback=2,Dispose=4,FinishActiveOperation=8");
        await Assert.That(typeof(DataLinqRecoveryActions).IsDefined(typeof(FlagsAttribute), false)).IsTrue();
        foreach (var type in new[] { typeof(DataLinqFailureContext), typeof(DataLinqSecondaryFailure) })
        {
            await Assert.That(type.IsSealed).IsTrue();
            await Assert.That(type.GetConstructors()).IsEmpty();
            await Assert.That(type.GetProperties().All(property => property.SetMethod is null)).IsTrue();
        }

        static string Values<T>() where T : struct, Enum => string.Join(",", Enum.GetValues<T>().Select(value => $"{value}={Convert.ToInt32(value)}"));
    }

    [Test]
    public async Task MappingUsesEvidenceAndDoesNotInventRollbackOrFuturePermissions()
    {
        foreach (var cause in Enum.GetValues<ExecutionFailureCause>())
            await Assert.That(PublicFailureMapping.Cause(cause).ToString()).IsEqualTo(cause.ToString());
        foreach (var operation in Enum.GetValues<ExecutionOperationKind>())
            await Assert.That(PublicFailureMapping.OperationKind(operation).ToString()).IsEqualTo(operation.ToString());
        foreach (var completion in Enum.GetValues<ExecutionCompletion>())
            await Assert.That(PublicFailureMapping.Completion(completion).ToString()).IsEqualTo(completion.ToString());
        await Assert.That(PublicFailureMapping.Stage(ExecutionFailureStage.Recovery, ExecutionOperationKind.Rollback)).IsEqualTo(DataLinqFailureStage.Rollback);
        await Assert.That(PublicFailureMapping.Stage(ExecutionFailureStage.Recovery, ExecutionOperationKind.Query)).IsEqualTo(DataLinqFailureStage.Unknown);
        await Assert.That(PublicFailureMapping.Stage(ExecutionFailureStage.Materialization, ExecutionOperationKind.Query)).IsEqualTo(DataLinqFailureStage.RowLoading);
        await Assert.That(PublicFailureMapping.Stage(ExecutionFailureStage.Finalization, ExecutionOperationKind.Commit)).IsEqualTo(DataLinqFailureStage.LocalFinalization);
        await Assert.That(PublicFailureMapping.Cause((ExecutionFailureCause)999)).IsEqualTo(DataLinqFailureCause.Unknown);
        await Assert.That(PublicFailureMapping.OperationKind((ExecutionOperationKind)999)).IsEqualTo(DataLinqOperationKind.Unknown);
        await Assert.That(PublicFailureMapping.Completion((ExecutionCompletion)999)).IsEqualTo(DataLinqCompletionOutcome.Unknown);
        await Assert.That(PublicFailureMapping.Stage((ExecutionFailureStage)999, ExecutionOperationKind.Unknown)).IsEqualTo(DataLinqFailureStage.Unknown);
        await Assert.That(PublicFailureMapping.Recovery((ExecutionRecoveryActions)999)).IsEqualTo(DataLinqRecoveryActions.None);
        await Assert.That(PublicFailureMapping.Recovery(ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose))
            .IsEqualTo(DataLinqRecoveryActions.Continue | DataLinqRecoveryActions.Rollback | DataLinqRecoveryActions.Dispose);
    }
}
