using System;
using System.Diagnostics;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    private T ExecuteSingleMutation<T>(Mutable<T> model, TransactionChangeType type, ExecutionOperationKind kind,
        bool hasBatchPrefix = false)
        where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        var snapshot = MutationPreflight.CaptureAndEnsure(this, model, type, kind);
        var change = new StateChange(model, model.Metadata().Table, type, snapshot);
        return ExecutePreflightedStateChange(change, kind, unchanged: type == TransactionChangeType.Update && snapshot.IsEmpty,
            hasBatchPrefix: hasBatchPrefix) as T
            ?? throw new ModelLoadFailureException(change.PrimaryKeys);
    }

    // Internal statement-only seam used for generated-value/converter verification.
    // It does not apply transaction-local cache or mutable lifecycle effects.
    internal void ExecutePreflightedStatement(StateChange change) =>
        _ = ExecutePreflightedStateChange(change, MutationOperationKind(change.Type), statementOnly: true);

    private IImmutableInstance? ExecutePreflightedStateChange(StateChange change, ExecutionOperationKind kind,
        bool unchanged = false, bool statementOnly = false, bool hasBatchPrefix = false)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using var operation = BeginExclusiveOperation("execute a mutation", operationKind: kind);
        successfulChanges.EnsureCapacity(successfulChanges.Count + 1);
        if (change.Model is IMutableLifecycle) touchedMutables.EnsureCapacity(touchedMutables.Count + 1);
        if (!unchanged && change.HasExecutionAttempted)
            throw new InvalidOperationException("This state change has already started provider execution and cannot be executed again.");

        var reportingScope = ExecutionFailureScope.Current;
        var telemetry = new MutationExecutionTelemetry(DataLinqTelemetryContext.FromProvider(Provider), change.Table.DbName, change.Type, Type, kind);
        var caller = Activity.Current;
        ExecutionFailures? failures = null;
        ExecutionFailureContext? executionContext = null;
        var mayHaveEffects = false;
        var affected = 0;
        var succeeded = false;
        var stage = ExecutionFailureStage.Validation;
        var mutationStage = TransactionFailureStage.ProviderStatement;
        IImmutableInstance? immutable = null;
        // Snapshot once so a newly attached listener cannot run outside its
        // callback scope. Mutation timing and completion metrics always start.
        var createActivity = DataLinqTelemetry.HasActivityListeners;
        using (ExecutionFailureScope.Call? reporting = createActivity ? ExecutionFailureScope.Begin() : null)
        {
            try { telemetry.Start(createActivity); }
            catch (Exception failure) { ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, kind); }
        }
        if (failures is null)
        {
            // Retain fresh nested reports, but not reports from earlier settled work.
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try
            {
                if (!unchanged)
                {
                    if (!change.TryBeginExecution())
                        throw new InvalidOperationException("This state change has already started provider execution and cannot be executed again.");
                    using (var step = ExecutionGate.EnterStep(operation))
                        affected = change.ExecuteReservedQuery(this, step, ref mayHaveEffects);
                    if (!statementOnly)
                    {
                        stage = ExecutionFailureStage.Finalization;
                        mutationStage = TransactionFailureStage.PendingCacheApplication;
                        occurrence = ExecutionFailureContexts.CaptureOccurrence();
                        Provider.State.ApplyChanges([change], this);
                        if (!change.HasSameFinalizedMutation())
                            throw new InvalidOperationException("The mutable assignments changed while the transaction-local cache effect was being applied.");
                    }
                }
                if (!statementOnly)
                {
                    stage = ExecutionFailureStage.RowLoading;
                    mutationStage = TransactionFailureStage.Hydration;
                    occurrence = ExecutionFailureContexts.CaptureOccurrence();
                    immutable = LoadAuthoritativeStateChange(change, operation);
                    if (!unchanged)
                    {
                        stage = ExecutionFailureStage.Finalization;
                        occurrence = ExecutionFailureContexts.CaptureOccurrence();
                        if (!change.HasSameFinalizedMutation())
                            throw new InvalidOperationException("The mutable assignments changed during authoritative-row hydration.");
                        change.FinalizeSuccessfulRelationKeys(immutable);
                        if (!change.HasSameFinalizedMutation())
                            throw new InvalidOperationException("The mutable assignments changed while finalizing authoritative relation impact keys.");
                        mutationStage = TransactionFailureStage.LifecycleFinalization;
                        FinalizeSuccessfulStateChange(change, immutable);
                        successfulChanges.Add(change);
                    }
                }
                succeeded = true;
            }
            catch (Exception failure)
            {
                ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
                executionContext = ExecutionFailureContexts.GetCurrent(failure);
                (failures ??= new(reportingScope)).AddObserved(new(failure, executionContext), stage,
                    stage == ExecutionFailureStage.Finalization ? ExecutionFailureCause.LocalFinalizationError : ExecutionFailureCause.Unknown, kind);
                if (mutationStage == TransactionFailureStage.ProviderStatement && change.ExecutionPhase == StateChangeExecutionPhase.Hydration)
                    mutationStage = TransactionFailureStage.Hydration;
            }
        }
        telemetry.Complete(ref failures, succeeded, affected);
        ExecutionActivity.RestoreCurrent(caller, ref failures, kind);
        if (failures?.Primary is { } primary)
        {
            if (succeeded) mutationStage = TransactionFailureStage.LifecycleFinalization;
            if ((mayHaveEffects || hasBatchPrefix) && !statementOnly)
                PoisonMutation(mutationStage, primary, mayHaveEffects ? change.Model : null);
            PublishSynchronousMutationFailure(failures, operation, kind, mayHaveEffects || hasBatchPrefix, unchanged, executionContext);
            failures.ThrowIfAny();
        }
        return immutable;
    }

    private void PublishSynchronousMutationFailure(ExecutionFailures failures, TransactionOperationGate.Lease operation,
        ExecutionOperationKind kind, bool mayHaveEffects, bool unchanged, ExecutionFailureContext? executionContext)
    {
        // The legacy synchronous transaction contract permits a rollback attempt
        // after an unclassified mutation failure. It never proves safe continuation.
        var evidence = new ReadFailureEvidence(Effects: mayHaveEffects ? ExecutionEffects.Mutation : ExecutionEffects.NoStatement,
            Integrity: mayHaveEffects ? TransactionIntegrity.Unknown : TransactionIntegrity.Confirmed, RollbackAvailable: true);
        var assessed = true;
        using (ExecutionFailureScope.Begin())
        {
            try
            {
                if (executionContext?.Stage == ExecutionFailureStage.Initialization ||
                    DatabaseAccess is IAsyncTransactionCompletion
                        { InitializationState: TransactionInitializationState.Failed or TransactionInitializationState.Disposed })
                {
                    evidence = new(Effects: ExecutionEffects.Initialization);
                }
                else if (DatabaseAccess is IAsyncReadFailureEvidence classifier)
                {
                    evidence = classifier.GetReadFailureEvidence(failures.Primary!)
                        ?? throw new InvalidOperationException("The provider returned no failure evidence.");
                    if (evidence.Effects != ExecutionEffects.Initialization)
                        evidence = mayHaveEffects ? evidence with { Effects = ExecutionEffects.Mutation }
                            : evidence with { Effects = ExecutionEffects.NoStatement,
                                Integrity = evidence.Integrity == TransactionIntegrity.Lost ? TransactionIntegrity.Lost : TransactionIntegrity.Confirmed };
                }
            }
            catch (Exception failure)
            {
                assessed = false;
                evidence = new();
                failures.Add(failure, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery, kind);
            }
        }
        var recovery = ExecutionRecoveryPolicy.ForReadFailure(evidence, assessed && !failures.HasCleanupFailure);
        // Unchanged-save hydration may already have a stricter read assessment.
        if (unchanged && executionContext is not null) recovery &= executionContext.Recovery;
        var context = failures.Snapshot(evidence, ExecutionCompletion.NotAttempted, recovery, TransactionID, kind, ExecutionGate.ProviderInstanceId);
        using (var owner = ExecutionGate.EnterStep(operation)) RecordAsyncReadFailure(owner, context);
        ExecutionFailureContexts.Attach(failures.Primary!, context);
        operation.ReportFailure(failures.Primary!);
    }
}
