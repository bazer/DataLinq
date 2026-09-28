using DataLinq.Execution;

namespace DataLinq.Diagnostics;

internal static class PublicFailureMapping
{
    internal static DataLinqFailureCause Cause(ExecutionFailureCause value) => value switch
    {
        ExecutionFailureCause.Cancellation => DataLinqFailureCause.Cancellation,
        ExecutionFailureCause.Timeout => DataLinqFailureCause.Timeout,
        ExecutionFailureCause.ProviderError => DataLinqFailureCause.ProviderError,
        ExecutionFailureCause.ApplicationError => DataLinqFailureCause.ApplicationError,
        ExecutionFailureCause.MaterializationError => DataLinqFailureCause.MaterializationError,
        ExecutionFailureCause.LocalFinalizationError => DataLinqFailureCause.LocalFinalizationError,
        ExecutionFailureCause.InvalidOperation => DataLinqFailureCause.InvalidOperation,
        _ => DataLinqFailureCause.Unknown
    };

    internal static DataLinqOperationKind OperationKind(ExecutionOperationKind value) => value switch
    {
        ExecutionOperationKind.Query => DataLinqOperationKind.Query,
        ExecutionOperationKind.KeyLookup => DataLinqOperationKind.KeyLookup,
        ExecutionOperationKind.RelationLoad => DataLinqOperationKind.RelationLoad,
        ExecutionOperationKind.Insert => DataLinqOperationKind.Insert,
        ExecutionOperationKind.Update => DataLinqOperationKind.Update,
        ExecutionOperationKind.Save => DataLinqOperationKind.Save,
        ExecutionOperationKind.Delete => DataLinqOperationKind.Delete,
        ExecutionOperationKind.Commit => DataLinqOperationKind.Commit,
        ExecutionOperationKind.Rollback => DataLinqOperationKind.Rollback,
        ExecutionOperationKind.Dispose => DataLinqOperationKind.Dispose,
        ExecutionOperationKind.TransactionCallback => DataLinqOperationKind.TransactionCallback,
        ExecutionOperationKind.RawCommand => DataLinqOperationKind.RawCommand,
        ExecutionOperationKind.MetadataRead => DataLinqOperationKind.MetadataRead,
        ExecutionOperationKind.SchemaValidation => DataLinqOperationKind.SchemaValidation,
        ExecutionOperationKind.ExistenceCheck => DataLinqOperationKind.ExistenceCheck,
        ExecutionOperationKind.Provisioning => DataLinqOperationKind.Provisioning,
        ExecutionOperationKind.ProviderConfiguration => DataLinqOperationKind.ProviderConfiguration,
        _ => DataLinqOperationKind.Unknown
    };

    internal static DataLinqCompletionOutcome Completion(ExecutionCompletion value) => value switch
    {
        ExecutionCompletion.NotApplicable => DataLinqCompletionOutcome.NotApplicable,
        ExecutionCompletion.NotAttempted => DataLinqCompletionOutcome.NotAttempted,
        ExecutionCompletion.Committed => DataLinqCompletionOutcome.Committed,
        ExecutionCompletion.RolledBack => DataLinqCompletionOutcome.RolledBack,
        _ => DataLinqCompletionOutcome.Unknown
    };

    internal static DataLinqFailureStage Stage(ExecutionFailureStage stage, ExecutionOperationKind operation) => stage switch
    {
        ExecutionFailureStage.Validation => DataLinqFailureStage.Validation,
        ExecutionFailureStage.Initialization => DataLinqFailureStage.Initialization,
        ExecutionFailureStage.CommandExecution => DataLinqFailureStage.CommandExecution,
        ExecutionFailureStage.RowLoading or ExecutionFailureStage.Materialization => DataLinqFailureStage.RowLoading,
        ExecutionFailureStage.Callback => DataLinqFailureStage.Callback,
        ExecutionFailureStage.Finalization => DataLinqFailureStage.LocalFinalization,
        ExecutionFailureStage.Commit => DataLinqFailureStage.Commit,
        ExecutionFailureStage.Recovery when operation == ExecutionOperationKind.Rollback => DataLinqFailureStage.Rollback,
        ExecutionFailureStage.CacheRecovery => DataLinqFailureStage.CacheRecovery,
        ExecutionFailureStage.Notification => DataLinqFailureStage.Notification,
        ExecutionFailureStage.Cleanup => DataLinqFailureStage.Cleanup,
        _ => DataLinqFailureStage.Unknown
    };

    internal static DataLinqRecoveryActions Recovery(ExecutionRecoveryActions actions)
    {
        const ExecutionRecoveryActions known = ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback |
            ExecutionRecoveryActions.Dispose | ExecutionRecoveryActions.FinishActiveOperation;
        // Never invent a public permission from an unfamiliar internal action.
        if ((actions & ~known) != 0) return DataLinqRecoveryActions.None;
        var result = DataLinqRecoveryActions.None;
        if ((actions & ExecutionRecoveryActions.Continue) != 0) result |= DataLinqRecoveryActions.Continue;
        if ((actions & ExecutionRecoveryActions.Rollback) != 0) result |= DataLinqRecoveryActions.Rollback;
        if ((actions & ExecutionRecoveryActions.Dispose) != 0) result |= DataLinqRecoveryActions.Dispose;
        if ((actions & ExecutionRecoveryActions.FinishActiveOperation) != 0) result |= DataLinqRecoveryActions.FinishActiveOperation;
        return result;
    }
}
