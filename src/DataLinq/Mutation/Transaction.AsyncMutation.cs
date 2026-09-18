using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        => ExecuteSingleMutationAsync(model, TransactionChangeType.Insert, token);

    internal Task<T> UpdateAsyncCore<T>(Mutable<T> model, CancellationToken token = default) where T : class, IImmutableInstance
        => ExecuteSingleMutationAsync(model, TransactionChangeType.Update, token);

    internal Task<T> SaveAsyncCore<T>(Mutable<T> model, CancellationToken token = default) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExecuteSingleMutationAsync(model, model.IsNew() ? TransactionChangeType.Insert : TransactionChangeType.Update, token);
    }

    internal Task<T> MutateWithEditsAsyncCore<T, TMutable>(TMutable model, Action<TMutable> edits,
        TransactionChangeType? type, CancellationToken token = default)
        where T : class, IImmutableInstance where TMutable : Mutable<T>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edits);
        var selected = type ?? (model.IsNew() ? TransactionChangeType.Insert : TransactionChangeType.Update);
        EnsureMutationPreflight(model, selected);
        token.ThrowIfCancellationRequested();
        edits(model);
        // Save follows the resulting lifecycle, just like the synchronous overload.
        return ExecuteSingleMutationAsync<T>(model,
            type ?? (model.IsNew() ? TransactionChangeType.Insert : TransactionChangeType.Update), token);
    }

    internal async Task DeleteAsyncCore(IModelInstance model, CancellationToken token = default)
    {
        using var input = CaptureMutation(model, TransactionChangeType.Delete);
        await ExecuteCapturedMutationsAsync([input], token).ConfigureAwait(false);
    }

    private async Task<T> ExecuteSingleMutationAsync<T>(Mutable<T> model, TransactionChangeType type, CancellationToken token)
        where T : class, IImmutableInstance
    {
        using var input = CaptureMutation(model, type);
        var results = await ExecuteCapturedMutationsAsync([input], token).ConfigureAwait(false);
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
                inputs.Add(CaptureMutation(model, TransactionChangeType.Insert));
            }
            var results = await ExecuteCapturedMutationsAsync(inputs, token).ConfigureAwait(false);
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
        var results = await ExecuteCapturedMutationsAsync([input], token).ConfigureAwait(false);
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

    private CapturedMutation CaptureMutation(IModelInstance model, TransactionChangeType type)
    {
        ArgumentNullException.ThrowIfNull(model);
        var snapshot = MutationPreflight.CaptureAndEnsure(this, model, type);
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

    private Task<List<IImmutableInstance?>> ExecuteCapturedMutationsAsync(IReadOnlyList<CapturedMutation> inputs, CancellationToken token)
        => ExecuteCapturedMutationsAsync(inputs, BindCapturedMutations(inputs), token);

    private async Task<List<IImmutableInstance?>> ExecuteCapturedMutationsAsync(IReadOnlyList<CapturedMutation> inputs,
        IAsyncSqlReaderFactory? readers, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var results = new List<IImmutableInstance?>(inputs.Count);
        using var operation = BeginExclusiveOperation("execute asynchronous mutations");
        using var owner = ExecutionGate.EnterStep(operation);
        EnsureCanRead("execute asynchronous mutations", owner);
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
            var telemetry = DataLinqTelemetryContext.FromProvider(Provider);
            using var activity = DataLinqTelemetry.StartMutationActivity(telemetry, change.Table.DbName, change.Type, Type);
            var started = Stopwatch.GetTimestamp();
            var succeeded = false;
            var affected = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!change.HasSameCapturedMutation()) throw new InvalidOperationException("The captured mutation inputs changed before execution.");
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
                        change.CompleteAsyncStatement(generated, input.Reservation);
                    }
                    else
                    {
                        affected = await input.Command!.ExecuteNonQueryAsync(owner, token).ConfigureAwait(false);
                        wrote = true;
                        lastWrittenModel = change.Model;
                        mutationStage = TransactionFailureStage.Hydration;
                        stage = ExecutionFailureStage.Materialization;
                        change.CompleteAsyncStatement(null, input.Reservation);
                    }
                    mutationStage = TransactionFailureStage.PendingCacheApplication;
                    Provider.State.ApplyChanges([change], this);
                    EnsureFinalizedInput(change);
                }

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
            catch (Exception failure)
            {
                var failures = new ExecutionFailures();
                failures.AddReported(failure, stage, failure is OperationCanceledException canceled &&
                    canceled.CancellationToken == token && token.IsCancellationRequested
                        ? ExecutionFailureCause.Cancellation : stage is ExecutionFailureStage.Materialization or ExecutionFailureStage.Finalization
                            ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown);
                var effects = wrote || input.Command?.Dispatched == true;
                var evidence = new ReadFailureEvidence(Effects: ExecutionEffects.NoStatement, Integrity: TransactionIntegrity.Confirmed);
                var assessed = true;
                try
                {
                    if (observedRead is not null) evidence = observedRead.GetReadFailureEvidence(failure);
                    else if (input.Command is not null) evidence = input.Command.GetReadFailureEvidence(failure);
                    if (evidence is null) throw new InvalidOperationException("The provider returned no failure evidence.");
                }
                catch (Exception assessment)
                {
                    assessed = false;
                    evidence = new();
                    failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
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
                var context = failures.Snapshot(evidence, ExecutionCompletion.NotAttempted, recovery, TransactionID);
                RecordAsyncReadFailure(owner, context);
                ExecutionFailureContexts.Attach(failure, context);
                operation.ReportFailure(failure);
                DataLinqTelemetry.RecordException(activity, failure);
                failures.ThrowIfAny();
                throw;
            }
            finally
            {
                DataLinqTelemetry.RecordMutationExecution(telemetry, change.Table.DbName, change.Type, Type, succeeded, affected, Stopwatch.GetElapsedTime(started));
                activity?.SetTag("datalinq.outcome", succeeded ? "success" : "failure");
                activity?.SetTag("db.operation.rows_affected", affected);
            }
        }
        return results;
    }

    private static void EnsureFinalizedInput(StateChange change)
    {
        if (!change.HasSameFinalizedMutation()) throw new InvalidOperationException("The mutable assignments changed during asynchronous mutation finalization.");
    }
}
