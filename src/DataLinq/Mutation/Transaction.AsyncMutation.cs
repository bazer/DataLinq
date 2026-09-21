using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    // Internal execution entries until native-provider and public-surface evidence.
    internal Task<T> InsertAsyncCore<T>(Mutable<T> model, CancellationToken token = default) where T : class, IImmutableInstance
        => ExecuteSingleMutationAsync(model, TransactionChangeType.Insert, ExecutionOperationKind.Insert, token);

    internal Task<T> UpdateAsyncCore<T>(Mutable<T> model, CancellationToken token = default) where T : class, IImmutableInstance
        => ExecuteSingleMutationAsync(model, TransactionChangeType.Update, ExecutionOperationKind.Update, token);

    internal Task<T> SaveAsyncCore<T>(Mutable<T> model, CancellationToken token = default) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExecuteSingleMutationAsync(model, model.IsNew() ? TransactionChangeType.Insert : TransactionChangeType.Update,
            ExecutionOperationKind.Save, token);
    }

    internal Task<T> MutateWithEditsAsyncCore<T, TMutable>(TMutable model, Action<TMutable> edits,
        TransactionChangeType? type, CancellationToken token = default)
        where T : class, IImmutableInstance where TMutable : Mutable<T>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edits);
        var selected = type ?? (model.IsNew() ? TransactionChangeType.Insert : TransactionChangeType.Update);
        var operationKind = MutationOperationKind(type);
        MutationPreflight.Ensure(this, model, selected, operationKind);
        token.ThrowIfCancellationRequested();
        edits(model);
        // Save follows the resulting lifecycle, just like the synchronous overload.
        return ExecuteSingleMutationAsync<T>(model,
            type ?? (model.IsNew() ? TransactionChangeType.Insert : TransactionChangeType.Update), operationKind, token);
    }

    internal async Task DeleteAsyncCore(IModelInstance model, CancellationToken token = default)
    {
        using var input = CaptureMutation(model, TransactionChangeType.Delete, ExecutionOperationKind.Delete);
        await ExecuteCapturedMutationsAsync([input], ExecutionOperationKind.Delete, token).ConfigureAwait(false);
    }

    private async Task<T> ExecuteSingleMutationAsync<T>(Mutable<T> model, TransactionChangeType type,
        ExecutionOperationKind operationKind, CancellationToken token)
        where T : class, IImmutableInstance
    {
        using var input = CaptureMutation(model, type, operationKind);
        var results = await ExecuteCapturedMutationsAsync([input], operationKind, token).ConfigureAwait(false);
        return results[0] as T ?? throw new ModelLoadFailureException(input.Change.PrimaryKeys);
    }

    internal async Task<List<T>> InsertAsyncCore<T>(IEnumerable<Mutable<T>> models, CancellationToken token = default)
        where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(models);
        EnsureMutationPreflight(TransactionChangeType.Insert, typeof(T));
        var inputs = new List<CapturedMutation>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            // Enumerate and capture all inputs once before any suspension or write.
            foreach (var model in models)
            {
                ArgumentNullException.ThrowIfNull(model);
                if (!seen.Add(model)) throw new ArgumentException("A mutation batch cannot contain the same mutable object twice.", nameof(models));
                inputs.Add(CaptureMutation(model, TransactionChangeType.Insert, ExecutionOperationKind.Insert));
            }
            var results = await ExecuteCapturedMutationsAsync(inputs, ExecutionOperationKind.Insert, token).ConfigureAwait(false);
            var typed = new List<T>(results.Count);
            for (var i = 0; i < results.Count; i++)
                typed.Add(results[i] as T ?? throw new ModelLoadFailureException(inputs[i].Change.PrimaryKeys));
            return typed;
        }
        finally { foreach (var input in inputs) input.Dispose(); }
    }

    internal async Task<IImmutableInstance?> ExecuteStateChangeAsyncCore(StateChange change, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        MutationPreflight.EnsureExecution(this, change);
        using var input = new CapturedMutation(change, new MutationInputReservation(change.Model), allowUnchangedLookup: false);
        var results = await ExecuteCapturedMutationsAsync([input], MutationOperationKind(change.Type), token).ConfigureAwait(false);
        return results[0];
    }

    private sealed class CapturedMutation(StateChange change, MutationInputReservation reservation,
        bool allowUnchangedLookup = true) : IDisposable
    {
        internal StateChange Change { get; } = change;
        internal MutationInputReservation Reservation { get; } = reservation;
        internal AsyncEagerCommand? Command { get; set; }
        internal bool Unchanged => allowUnchangedLookup && Change.Type == TransactionChangeType.Update && Change.Snapshot.IsEmpty;
        internal bool NeedsHydration => Change.Type != TransactionChangeType.Delete && Change.Model is IMutableLifecycle;
        public void Dispose() => Reservation.Dispose();
    }

    private CapturedMutation CaptureMutation(IModelInstance model, TransactionChangeType type, ExecutionOperationKind operationKind)
    {
        ArgumentNullException.ThrowIfNull(model);
        var snapshot = MutationPreflight.CaptureAndEnsure(this, model, type, operationKind);
        var reservation = new MutationInputReservation(model);
        try
        {
            var change = new StateChange(model, model.Metadata().Table, type, snapshot);
            if (!change.HasSameCapturedMutation()) throw new InvalidOperationException("The mutable changed while its mutation was being captured.");
            return new(change, reservation);
        }
        catch { reservation.Dispose(); throw; }
    }

    private IAsyncSqlReaderFactory? BindCapturedMutations(IReadOnlyList<CapturedMutation> inputs)
    {
        // Capture both policies before awaiting. Later generated keys can change the
        // hydration SQL, but cannot rediscover a different reader policy.
        IAsyncMutationCommandFactory? commands = null;
        IAsyncSqlReaderFactory? readers = null;
        foreach (var input in inputs)
        {
            if (!input.Unchanged)
            {
                commands ??= IAsyncMutationCommandFactory.Require(DatabaseAccess);
                input.Command = commands.BindMutation(input.Change.CaptureAsyncStatement(this))
                    ?? throw new InvalidOperationException("Mutation binding returned no command.");
                input.Command.Validate(input.Change.NeedsGeneratedValue ? AsyncCommandKind.Scalar : AsyncCommandKind.NonQuery, hasOwner: true);
            }
            if (input.NeedsHydration)
                readers ??= IAsyncSqlReaderFactory.Require(DatabaseAccess).CaptureInvocation();
        }
        return readers;
    }

    private Task<List<IImmutableInstance?>> ExecuteCapturedMutationsAsync(IReadOnlyList<CapturedMutation> inputs,
        ExecutionOperationKind operationKind, CancellationToken token)
        => ExecuteCapturedMutationsAsync(inputs, BindCapturedMutations(inputs), operationKind, token);

    private async Task<List<IImmutableInstance?>> ExecuteCapturedMutationsAsync(IReadOnlyList<CapturedMutation> inputs,
        IAsyncSqlReaderFactory? readers, ExecutionOperationKind operationKind, CancellationToken token)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        token.ThrowIfCancellationRequested();
        var results = new List<IImmutableInstance?>(inputs.Count);
        using var operation = BeginExclusiveOperation("execute asynchronous mutations", operationKind: operationKind);
        using var owner = ExecutionGate.EnterStep(operation);
        EnsureCanRead("execute asynchronous mutations", owner, operationKind);
        successfulChanges.EnsureCapacity(successfulChanges.Count + inputs.Count);
        touchedMutables.EnsureCapacity(touchedMutables.Count + inputs.Count);
        var wrote = false;
        IModelInstance? lastWrittenModel = null;
        foreach (var input in inputs)
        {
            var change = input.Change;
            IAsyncReadFailureEvidence? observedRead = null;
            var stage = ExecutionFailureStage.Validation;
            var mutationStage = TransactionFailureStage.ProviderStatement;
            var telemetry = new MutationExecutionTelemetry(DataLinqTelemetryContext.FromProvider(Provider),
                change.Table.DbName, change.Type, Type, operationKind);
            ExecutionFailures? failures = null;
            var succeeded = false;
            var affected = 0;
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try
            {
                token.ThrowIfCancellationRequested();
                if (!change.HasSameCapturedMutation()) throw new InvalidOperationException("The captured mutation inputs changed before execution.");
                stage = ExecutionFailureStage.Notification;
                mutationStage = TransactionFailureStage.LifecycleFinalization;
                telemetry.Start();
                occurrence = ExecutionFailureContexts.CaptureOccurrence();
                stage = ExecutionFailureStage.Validation;
                mutationStage = TransactionFailureStage.ProviderStatement;
                if (!input.Unchanged)
                {
                    if (!change.TryBeginExecution()) throw new InvalidOperationException("This state change has already started execution.");
                    stage = ExecutionFailureStage.CommandExecution;
                    if (change.NeedsGeneratedValue)
                    {
                        var generated = await input.Command!.ExecuteScalarAsync(owner, token).ConfigureAwait(false);
                        wrote = true;
                        lastWrittenModel = change.Model;
                        affected = 1;
                        mutationStage = TransactionFailureStage.Hydration;
                        stage = ExecutionFailureStage.Materialization;
                        occurrence = ExecutionFailureContexts.CaptureOccurrence();
                        change.CompleteAsyncStatement(generated, input.Reservation);
                    }
                    else
                    {
                        affected = await input.Command!.ExecuteNonQueryAsync(owner, token).ConfigureAwait(false);
                        wrote = true;
                        lastWrittenModel = change.Model;
                        mutationStage = TransactionFailureStage.Hydration;
                        stage = ExecutionFailureStage.Materialization;
                        occurrence = ExecutionFailureContexts.CaptureOccurrence();
                        change.CompleteAsyncStatement(null, input.Reservation);
                    }
                    mutationStage = TransactionFailureStage.PendingCacheApplication;
                    stage = ExecutionFailureStage.Finalization;
                    occurrence = ExecutionFailureContexts.CaptureOccurrence();
                    Provider.State.ApplyChanges([change], this);
                    EnsureFinalizedInput(change);
                }

                occurrence = ExecutionFailureContexts.CaptureOccurrence();
                IImmutableInstance? immutable = null;
                if (input.NeedsHydration)
                {
                    mutationStage = TransactionFailureStage.Hydration;
                    stage = ExecutionFailureStage.RowLoading;
                    immutable = await Provider.GetTableCache(change.Table).GetProviderRowAsyncCore(change.PrimaryKeys, this, token,
                        owner, readers, read => observedRead = read).ConfigureAwait(false)
                        ?? throw new ModelLoadFailureException(change.PrimaryKeys);
                }
                // No cancellation checkpoints inside the short consistency section.
                // Settled statement/hydration reports cannot classify later local work.
                occurrence = ExecutionFailureContexts.CaptureOccurrence();
                if (!input.Unchanged)
                {
                    mutationStage = TransactionFailureStage.LifecycleFinalization;
                    stage = ExecutionFailureStage.Finalization;
                    EnsureFinalizedInput(change);
                    change.FinalizeSuccessfulRelationKeys(immutable);
                    EnsureFinalizedInput(change);
                    FinalizeSuccessfulStateChange(change, immutable);
                    successfulChanges.Add(change);
                }
                results.Add(immutable);
                succeeded = true;
            }
            catch (Exception executionFailure)
            {
                ExecutionFailureContexts.DiscardEarlierReport(executionFailure, occurrence);
                failures = new ExecutionFailures();
                failures.AddReported(executionFailure, stage, executionFailure is OperationCanceledException canceled &&
                    canceled.CancellationToken == token && token.IsCancellationRequested
                        ? ExecutionFailureCause.Cancellation : stage is ExecutionFailureStage.Finalization or ExecutionFailureStage.Notification
                            ? ExecutionFailureCause.LocalFinalizationError : stage == ExecutionFailureStage.Materialization
                                ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown, operationKind);
            }
            // Reporting is fallible local finalization under the same owner and
            // reservations. Collect it before publishing recovery or releasing admission.
            telemetry.Complete(ref failures, succeeded, affected);
            if (failures?.Primary is { } failure)
            {
                if (succeeded) mutationStage = TransactionFailureStage.LifecycleFinalization;
                var effects = wrote || input.Command?.Dispatched == true;
                var evidence = new ReadFailureEvidence(Effects: ExecutionEffects.NoStatement, Integrity: TransactionIntegrity.Confirmed);
                var assessed = true;
                if ((observedRead ?? input.Command) is { } classifier)
                {
                    using var assessmentDiagnostics = ExecutionFailureScope.Begin();
                    try
                    {
                        evidence = classifier.GetReadFailureEvidence(failure)
                            ?? throw new InvalidOperationException("The provider returned no failure evidence.");
                    }
                    catch (Exception assessment)
                    {
                        assessed = false;
                        evidence = new();
                        failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery, operationKind);
                    }
                }
                if (effects)
                {
                    // A canceled later batch input that never dispatched has not
                    // lost its baseline. Invalidate the affected prefix/current write.
                    PoisonMutation(mutationStage, failure, input.Command?.Dispatched == true
                        ? change.Model : lastWrittenModel!);
                    if (evidence.Effects != ExecutionEffects.Initialization) evidence = evidence with { Effects = ExecutionEffects.Mutation };
                }
                var recovery = ExecutionRecoveryPolicy.ForReadFailure(evidence, assessed && !failures.HasCleanupFailure);
                var context = failures.Snapshot(evidence, ExecutionCompletion.NotAttempted, recovery, TransactionID,
                    operationKind, owner.ProviderInstanceId);
                RecordAsyncReadFailure(owner, context);
                ExecutionFailureContexts.Attach(failure, context);
                operation.ReportFailure(failure);
                failures.ThrowIfAny();
            }
        }
        return results;
    }

    private static void EnsureFinalizedInput(StateChange change)
    {
        if (!change.HasSameFinalizedMutation()) throw new InvalidOperationException("The mutable assignments changed during asynchronous mutation finalization.");
    }

    // Save is a requested operation, not a statement type. Never derive it from
    // the selected insert/update after input capture or hydration has begun.
    private static ExecutionOperationKind MutationOperationKind(TransactionChangeType? type) => type switch
    {
        null => ExecutionOperationKind.Save,
        TransactionChangeType.Insert => ExecutionOperationKind.Insert,
        TransactionChangeType.Update => ExecutionOperationKind.Update,
        TransactionChangeType.Delete => ExecutionOperationKind.Delete,
        _ => ExecutionOperationKind.Unknown
    };
}
