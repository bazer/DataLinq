using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    internal Task<T> RunMutationHelperAsyncCore<T>(Mutable<T> model, TransactionChangeType? type, CancellationToken token)
        where T : class, IImmutableInstance
        => RunOwnedMutationAsync(model, type, (input, result) =>
            result as T ?? throw new ModelLoadFailureException(input.Change.PrimaryKeys), token);

    internal Task RunDeleteHelperAsyncCore(IModelInstance model, CancellationToken token)
        => RunOwnedMutationAsync(model, TransactionChangeType.Delete, static (_, _) => true, token);

    private async Task<TResult> RunOwnedMutationAsync<TResult>(IModelInstance model, TransactionChangeType? type,
        Func<CapturedMutation, IImmutableInstance?, TResult> select, CancellationToken token)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        // Construction is lazy and I/O-free. An unsupported adapter cannot acquire
        // resources through this helper; never substitute synchronous completion.
        var resource = new ManagedAsyncCompletion(this, RequireAsyncCompletion());
        var settings = new RecoveryRollbackSettings();
        CapturedMutation? input = null;
        var operationKind = MutationOperationKind(type);
        try
        {
            ArgumentNullException.ThrowIfNull(model);
            var selected = type ?? (model is IMutableInstance mutable && mutable.IsNew()
                ? TransactionChangeType.Insert : TransactionChangeType.Update);
            input = CaptureMutation(model, selected, operationKind);
            CapturedMutation[] inputs = [input];
            var readers = BindCapturedMutations(inputs);
            return await RunCallbackAsyncCore(async cancellation =>
            {
                var results = await ExecuteCapturedMutationsAsync(inputs, readers, operationKind, cancellation).ConfigureAwait(false);
                return select(input, results[0]);
            }, settings, token, callbackOperation: operationKind).ConfigureAwait(false);
        }
        catch (Exception failure) when (!IsDisposed)
        {
            // Preparation and callback-capability validation can fail before the
            // callback runner takes ownership. The database helper still owns this
            // transaction, and must retain the original failure through cleanup.
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Validation, fallbackOperation: operationKind);
            using var operation = BeginExclusiveOperation("clean up failed mutation helper", completion: true, operationKind: operationKind);
            var actions = ExecutionRecoveryActions.Dispose;
            using (ExecutionFailureScope.Begin())
            {
                try { actions = resource.Recovery; }
                catch (Exception assessment) { failures.AddReported(assessment, ExecutionFailureStage.Recovery, fallbackOperation: operationKind); }
            }
            var cleanup = new AutomaticTransactionRecovery(ExecutionGate, operation, resource, settings, failures,
                resource.Completion, actions, TransactionID);
            try { await cleanup.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                if (cleanup.FailureContext is { } context) Volatile.Write(ref asyncFailureContext, context);
                if (IsDisposed) UpdateAsyncRecovery(resource.Completion, ExecutionRecoveryActions.None);
            }
            throw;
        }
        finally
        {
            // The helper result/failure becomes observable only after completion and
            // both cleanup attempts; releasing never repairs an invalid baseline.
            input?.Dispose();
        }
    }
}
