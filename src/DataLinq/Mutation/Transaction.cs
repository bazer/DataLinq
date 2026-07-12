using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Mutation;

/// <summary>
/// Enumerates the types of transactions.
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// Transaction that allows both read and write operations.
    /// </summary>
    ReadAndWrite,
    /// <summary>
    /// Transaction that only allows read operations.
    /// </summary>
    ReadOnly,
    /// <summary>
    /// Transaction that only allows write operations.
    /// </summary>
    WriteOnly
}

/// <summary>
/// Enumerates the types of changes that can be made to a transaction.
/// </summary>
public enum TransactionChangeType
{
    /// <summary>
    /// Insert a new row into the database.
    /// </summary>
    Insert,
    /// <summary>
    /// Update an existing row in the database.
    /// </summary>
    Update,
    /// <summary>
    /// Delete an existing row from the database.
    /// </summary>
    Delete
}

/// <summary>
/// Provides data for the <see cref="Transaction.OnStatusChanged"/> event.
/// </summary>
public class TransactionStatusChangeEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionStatusChangeEventArgs"/> class.
    /// </summary>
    /// <param name="transaction">The transaction that raised the event.</param>
    /// <param name="status">The new status of the transaction.</param>
    public TransactionStatusChangeEventArgs(Transaction transaction, DatabaseTransactionStatus status)
    {
        Transaction = transaction;
        Status = status;
    }

    /// <summary>
    /// Gets the transaction that raised the event.
    /// </summary>
    public Transaction Transaction { get; }

    /// <summary>
    /// Gets the new status of the transaction.
    /// </summary>
    public DatabaseTransactionStatus Status { get; }
}

internal enum TransactionFailureStage
{
    ProviderStatement,
    Hydration,
    PendingCacheApplication,
    LifecycleFinalization
}

internal sealed record TransactionFailure(
    TransactionFailureStage Stage,
    Exception Cause);

/// <summary>
/// Represents a database transaction.
/// </summary>
public class Transaction : DataSourceAccess, IDisposable, IEquatable<Transaction>
{
    private static uint transactionCount = 0;
    private readonly List<StateChange> successfulChanges = [];
    private readonly HashSet<IMutableLifecycle> touchedMutables =
        new(ReferenceEqualityComparer.Instance);
    private readonly bool isAttachedTransaction;
    private TransactionFailure? failure;
    private int exclusiveOperationState;
    private int internalReadThreadId;
    private int managedCommitFinalizationState;
    private int deferredCommittedStatus;
    private int managedRollbackFinalizationState;
    private int deferredRolledBackStatus;
    private int managedRollbackAttempted;
    private int disposeState;
    internal bool IsDisposed => Volatile.Read(ref disposeState) != 0;
    internal TransactionFailure? Failure => Volatile.Read(ref failure);
    internal bool IsPoisoned => Failure is not null;

    /// <summary>
    /// Gets the ID of the transaction.
    /// </summary>
    public uint TransactionID { get; }

    internal MutableTransactionOwnership MutableOwnership { get; }
    internal IReadOnlyCollection<IMutableLifecycle> TouchedMutables => touchedMutables;
    internal IReadOnlyList<StateChange> SuccessfulChanges => successfulChanges;

    /// <summary>
    /// Gets an ordered snapshot of state changes that completed successfully.
    /// </summary>
    public List<StateChange> Changes => new(successfulChanges);

    /// <summary>
    /// Gets the type of the transaction.
    /// </summary>
    public TransactionType Type { get; protected set; }

    /// <summary>
    /// Gets the status of the database transaction.
    /// </summary>
    public DatabaseTransactionStatus Status => DatabaseAccess.Status;

    public override DatabaseTransaction DatabaseAccess { get; }

    /// <summary>
    /// Occurs when the managed transaction status changes. For commits initiated through
    /// <see cref="Commit"/>, the committed notification is raised only after committed cache
    /// publication, transaction-local cache cleanup, and mutable baseline promotion have
    /// completed. For rollback and disposal of an open transaction, the rolled-back notification
    /// is raised only after transaction-local cleanup and mutable invalidation. Direct low-level
    /// completion through <see cref="DatabaseAccess"/> retains provider timing and bypasses this
    /// managed finalization contract.
    /// </summary>
    public event EventHandler<TransactionStatusChangeEventArgs>? OnStatusChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction"/> class.
    /// </summary>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="type">The type of the transaction.</param>
    public Transaction(IDatabaseProvider databaseProvider, TransactionType type) : base(databaseProvider)
    {
        //Provider = databaseProvider;
        DatabaseAccess = databaseProvider.GetNewDatabaseTransaction(type);
        DatabaseAccess.OnStatusChanged += HandleDatabaseStatusChanged;
        Type = type;
        isAttachedTransaction = false;

        TransactionID = Interlocked.Increment(ref transactionCount);
        MutableOwnership = new MutableTransactionOwnership(databaseProvider, TransactionID);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction"/> class.
    /// </summary>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="dbTransaction">The database transaction.</param>
    /// <param name="type">The type of the transaction.</param>
    public Transaction(IDatabaseProvider databaseProvider, IDbTransaction dbTransaction, TransactionType type) : base(databaseProvider)
    {
        //Provider = databaseProvider;
        DatabaseAccess = databaseProvider.AttachDatabaseTransaction(dbTransaction, type);
        DatabaseAccess.OnStatusChanged += HandleDatabaseStatusChanged;
        Type = type;
        isAttachedTransaction = true;

        TransactionID = Interlocked.Increment(ref transactionCount);
        MutableOwnership = new MutableTransactionOwnership(databaseProvider, TransactionID);
    }

    /// <summary>
    /// Inserts a new row into the database.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="model">The model to insert.</param>
    /// <returns>The inserted model.</returns>
    public T Insert<T>(Mutable<T> model) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureMutationPreflight(model, TransactionChangeType.Insert);

        var change = new StateChange(
            model,
            model.Metadata().Table,
            TransactionChangeType.Insert);
        return ExecuteStateChange(change) as T ??
            throw new ModelLoadFailureException(change.PrimaryKeys);
    }

    /// <summary>
    /// Applies changes to a new mutable row and inserts it.
    /// </summary>
    public T Insert<T>(Mutable<T> model, Action<Mutable<T>> changes)
        where T : class, IImmutableInstance
    {
        return Insert<T, Mutable<T>>(model, changes);
    }

    /// <summary>
    /// Applies changes to a generated mutable row and inserts it.
    /// </summary>
    public T Insert<T, TMutable>(TMutable model, Action<TMutable> changes)
        where T : class, IImmutableInstance
        where TMutable : Mutable<T>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);
        EnsureMutationPreflight(model, TransactionChangeType.Insert);

        changes(model);

        return Insert<T>(model);
    }

    /// <summary>
    /// Inserts multiple new rows into the database.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="models">The models to insert.</param>
    /// <returns>The inserted models.</returns>
    public List<T> Insert<T>(IEnumerable<Mutable<T>> models) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(models);
        EnsureMutationPreflight(TransactionChangeType.Insert, typeof(T));

        return models
            .Select(Insert)
            .ToList();
    }

    /// <summary>
    /// Updates an existing row in the database.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="model">The model to update.</param>
    /// <returns>The updated model.</returns>
    public T Update<T>(Mutable<T> model) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureMutationPreflight(model, TransactionChangeType.Update);

        // If there are no changes to save, skip saving and return the model from the cache directly.
        if (!model.GetChanges().Any())
            return GetModelFromCache(model) ?? throw new ModelLoadFailureException(model.PrimaryKeys());

        var change = new StateChange(
            model,
            model.Metadata().Table,
            TransactionChangeType.Update);
        return ExecuteStateChange(change) as T ??
            throw new ModelLoadFailureException(change.PrimaryKeys);
    }

    /// <summary>
    /// Applies changes to an existing mutable row and updates it.
    /// </summary>
    public T Update<T>(Mutable<T> model, Action<Mutable<T>> changes)
        where T : class, IImmutableInstance
    {
        return Update<T, Mutable<T>>(model, changes);
    }

    /// <summary>
    /// Applies changes to a generated mutable row and updates it.
    /// </summary>
    public T Update<T, TMutable>(TMutable model, Action<TMutable> changes)
        where T : class, IImmutableInstance
        where TMutable : Mutable<T>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);
        EnsureMutationPreflight(model, TransactionChangeType.Update);

        changes(model);

        return Update<T>(model);
    }

    /// <summary>
    /// Updates an existing row in the database with the specified changes.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="model">The model to update.</param>
    /// <param name="changes">The changes to apply to the model.</param>
    /// <returns>The updated model.</returns>
    public T Update<T>(T model, Action<Mutable<T>> changes) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);
        var mut = new Mutable<T>(model);

        return Update(mut, changes);
    }

    /// <summary>
    /// Inserts a new row into the database or updates an existing row if it already exists.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="model">The model to insert or update.</param>
    /// <returns>The inserted or updated model.</returns>
    public T Save<T>(Mutable<T> model) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsNew())
            return Insert(model);
        else
            return Update(model);
    }

    /// <summary>
    /// Inserts a new row into the database or updates an existing row if it already exists with the specified changes.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="model">The model to insert or update.</param>
    /// <param name="changes">The changes to apply to the model.</param>
    /// <returns>The inserted or updated model.</returns>
    public T Save<T>(T model, Action<Mutable<T>> changes) where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(changes);
        var mut = model == null
            ? new Mutable<T>()
            : new Mutable<T>(model);

        return Save(mut, changes);
    }

    /// <summary>
    /// Inserts a new row into the database or updates an existing row if it already exists with the specified changes.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="model">The model to insert or update.</param>
    /// <param name="changes">The changes to apply to the model.</param>
    /// <returns>The inserted or updated model.</returns>
    public T Save<T>(Mutable<T> model, Action<Mutable<T>> changes) where T : class, IImmutableInstance
    {
        var mut = model ?? new Mutable<T>();

        return Save<T, Mutable<T>>(mut, changes);
    }

    /// <summary>
    /// Applies changes to a generated mutable row and inserts or updates it according to its lifecycle.
    /// </summary>
    public T Save<T, TMutable>(TMutable model, Action<TMutable> changes)
        where T : class, IImmutableInstance
        where TMutable : Mutable<T>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);
        var operation = model.IsNew()
            ? TransactionChangeType.Insert
            : TransactionChangeType.Update;
        EnsureMutationPreflight(model, operation);

        changes(model);

        return Save<T>(model);
    }

    /// <summary>
    /// Deletes an existing row from the database.
    /// </summary>
    /// <param name="model">The model to delete.</param>
    public void Delete(IModelInstance model)
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureMutationPreflight(model, TransactionChangeType.Delete);

        _ = ExecuteStateChange(new StateChange(
            model,
            model.Metadata().Table,
            TransactionChangeType.Delete));
    }

    /// <summary>
    /// Gets models from a query.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <returns>The models returned by the query.</returns>
    public override IEnumerable<T> GetFromQuery<T>(string query)
    {
        EnsureCanRead("execute a query");
        var table = Provider.Metadata.GetTableModel(typeof(T)).Table;

        foreach (var reader in DatabaseAccess.ReadReader(query))
        {
            var rowData = new RowData(reader, table, table.Columns, false);
            yield return InstanceFactory.NewImmutableRow<T>(rowData, this);
        }
    }

    /// <summary>
    /// Gets models from a command.
    /// </summary>
    /// <typeparam name="T">The type of the model.</typeparam>
    /// <param name="dbCommand">The command to execute.</param>
    /// <returns>The models returned by the command.</returns>
    public override IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand)
    {
        EnsureCanRead("execute a command query");
        var table = Provider.Metadata.GetTableModel(typeof(T)).Table;

        foreach (var reader in DatabaseAccess.ReadReader(dbCommand))
        {
            var rowData = new RowData(reader, table, table.Columns, false);
            yield return InstanceFactory.NewImmutableRow<T>(rowData, this);
        }
    }

    internal IImmutableInstance? ExecuteStateChange(StateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MutationPreflight.EnsureExecution(this, change);
        BeginExclusiveOperation("execute a mutation");
        try
        {
            successfulChanges.EnsureCapacity(successfulChanges.Count + 1);
            if (change.Model is IMutableLifecycle)
                touchedMutables.EnsureCapacity(touchedMutables.Count + 1);

            if (!change.TryBeginExecution())
            {
                throw new InvalidOperationException(
                    "This state change has already started provider execution and cannot be executed again.");
            }

            var failureStage = TransactionFailureStage.ProviderStatement;
            try
            {
                change.ExecuteReservedQuery(this);

                failureStage = TransactionFailureStage.PendingCacheApplication;
                Provider.State.ApplyChanges([change], this);
                if (!change.HasSameFinalizedMutation())
                {
                    throw new InvalidOperationException(
                        "The mutable assignments changed while the transaction-local cache effect was being applied.");
                }

                failureStage = TransactionFailureStage.Hydration;
                var immutable = LoadAuthoritativeStateChange(change);
                if (!change.HasSameFinalizedMutation())
                {
                    throw new InvalidOperationException(
                        "The mutable assignments changed during authoritative-row hydration.");
                }
                change.FinalizeSuccessfulRelationKeys(immutable);
                if (!change.HasSameFinalizedMutation())
                {
                    throw new InvalidOperationException(
                        "The mutable assignments changed while finalizing authoritative relation impact keys.");
                }

                failureStage = TransactionFailureStage.LifecycleFinalization;
                FinalizeSuccessfulStateChange(change, immutable);

                successfulChanges.Add(change);
                return immutable;
            }
            catch (Exception exception)
            {
                if (failureStage == TransactionFailureStage.ProviderStatement &&
                    change.ExecutionPhase == StateChangeExecutionPhase.Hydration)
                {
                    failureStage = TransactionFailureStage.Hydration;
                }

                PoisonMutation(failureStage, exception, change.Model);
                throw;
            }
        }
        finally
        {
            EndExclusiveOperation();
        }
    }

    private IImmutableInstance? LoadAuthoritativeStateChange(StateChange change)
    {
        if (change.Type == TransactionChangeType.Delete ||
            change.Model is not IMutableLifecycle)
        {
            return null;
        }

        BeginInternalRead();
        try
        {
            return Provider
                .GetTableCache(change.Table)
                .GetRow(change.PrimaryKeys, this) ??
                throw new ModelLoadFailureException(change.PrimaryKeys);
        }
        finally
        {
            EndInternalRead();
        }
    }

    private void FinalizeSuccessfulStateChange(
        StateChange change,
        IImmutableInstance? immutable)
    {
        if (change.Type == TransactionChangeType.Delete)
        {
            if (change.Model is IMutableLifecycle mutableLifecycle)
            {
                mutableLifecycle.MarkDeleted(MutableOwnership);
                RegisterTouchedMutable(mutableLifecycle);
            }
            else if (change.Model is IMutableInstance mutable)
            {
                mutable.SetDeleted();
            }

            return;
        }

        if (change.Model is not IMutableLifecycle lifecycle)
            return;

        lifecycle.AdvanceBaseline(
            immutable ??
            throw new ModelLoadFailureException(change.PrimaryKeys),
            MutableOwnership);
        RegisterTouchedMutable(lifecycle);
    }

    private void PoisonMutation(
        TransactionFailureStage stage,
        Exception cause,
        IModelInstance currentModel)
    {
        Interlocked.CompareExchange(
            ref failure,
            new TransactionFailure(stage, cause),
            comparand: null);

        foreach (var touchedMutable in touchedMutables)
            touchedMutable.Invalidate(MutableInvalidationReason.MutationFailed);

        if (currentModel is IMutableLifecycle currentMutable)
            currentMutable.Invalidate(MutableInvalidationReason.MutationFailed);
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    public void Commit()
    {
        BeginExclusiveOperation("commit");
        try
        {
            EnsureTransactionCanComplete("commit", rejectPoisoned: true);
            Volatile.Write(ref managedCommitFinalizationState, 1);
            try
            {
                try
                {
                    DatabaseAccess.Commit();
                }
                catch (Exception providerFailure)
                {
                    var recoveryFailures = FinalizeCommitOutcomeUnknownState();
                    AddManagedCompletionFailureContext(
                        providerFailure,
                        operation: "Commit",
                        MutableInvalidationReason.CommitOutcomeUnknown,
                        recoveryFailures);
                    throw;
                }

                try
                {
                    Provider.State.ApplyChanges(successfulChanges);
                    Provider.State.RemoveTransactionFromCache(this);
                    PromoteTouchedMutablesAfterCommit();
                }
                catch (Exception finalizationFailure)
                {
                    ThrowCommittedStateFinalizationFailure(finalizationFailure);
                }

                Volatile.Write(ref managedCommitFinalizationState, 2);
                PublishDeferredCommittedStatus();
            }
            finally
            {
                Volatile.Write(ref deferredCommittedStatus, 0);
                Volatile.Write(ref managedCommitFinalizationState, 0);
            }
        }
        finally
        {
            EndExclusiveOperation();
        }
    }

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    public void Rollback()
    {
        BeginExclusiveOperation("roll back");
        try
        {
            EnsureTransactionCanComplete("roll back", rejectPoisoned: false);
            Volatile.Write(ref managedRollbackAttempted, 1);
            Volatile.Write(ref managedRollbackFinalizationState, 1);
            try
            {
                var attachedRollbackWasAmbiguous =
                    IsAttachedRollbackOutcomeAmbiguous();
                Exception? providerFailure = null;
                try
                {
                    DatabaseAccess.Rollback();
                }
                catch (Exception exception)
                {
                    providerFailure = exception;
                }

                var commitOutcomeUnknown =
                    MutableOwnership.Outcome == MutableTransactionOutcome.CommitOutcomeUnknown;
                var rolledBack = Status == DatabaseTransactionStatus.RolledBack;
                var outcome = commitOutcomeUnknown
                    ? MutableTransactionOutcome.CommitOutcomeUnknown
                    : attachedRollbackWasAmbiguous || !rolledBack
                        ? MutableTransactionOutcome.RollbackOutcomeUnknown
                        : MutableTransactionOutcome.RolledBack;
                var invalidationReason = commitOutcomeUnknown
                    ? MutableInvalidationReason.CommitOutcomeUnknown
                    : attachedRollbackWasAmbiguous || !rolledBack
                        ? MutableInvalidationReason.RollbackOutcomeUnknown
                        : MutableInvalidationReason.RolledBack;
                if (outcome != MutableTransactionOutcome.RolledBack &&
                    providerFailure is null)
                {
                    providerFailure = CreateRollbackOutcomeFailure(
                        commitOutcomeUnknown,
                        attachedRollbackWasAmbiguous,
                        rolledBack);
                }

                var cleanupFailures = FinalizeUncommittedState(
                    outcome,
                    invalidationReason);

                Volatile.Write(ref managedRollbackFinalizationState, 2);
                var observerFailure = CaptureDeferredRolledBackStatusFailure();
                ThrowManagedCompletionFailures(
                    operation: "Rollback",
                    invalidationReason,
                    providerFailure,
                    cleanupFailures,
                    observerFailure);
            }
            finally
            {
                Volatile.Write(ref deferredRolledBackStatus, 0);
                Volatile.Write(ref managedRollbackFinalizationState, 0);
            }
        }
        finally
        {
            EndExclusiveOperation();
        }
    }

    private T? GetModelFromCache<T>(Mutable<T> model) where T : class, IImmutableInstance
    {
        var metadata = model.Metadata();
        var keys = model.PrimaryKeys();

        return (T?)Provider.GetTableCache(metadata.Table).GetRow(keys, this);
    }

    private void RegisterTouchedMutable(IMutableLifecycle mutable) =>
        touchedMutables.Add(mutable);

    private void PromoteTouchedMutablesAfterCommit()
    {
        foreach (var touchedMutable in touchedMutables)
        {
            if (!touchedMutable.TryPromoteCommitted(MutableOwnership))
            {
                touchedMutable.Invalidate(
                    MutableInvalidationReason.CommittedStateFinalizationFailed);
            }
        }

        MutableOwnership.MarkCommittedAfterPublication();
        touchedMutables.Clear();
    }

    private void ThrowCommittedStateFinalizationFailure(Exception finalizationFailure)
    {
        MutableOwnership.MarkCommittedStateFinalizationFailed();
        foreach (var touchedMutable in touchedMutables)
        {
            touchedMutable.Invalidate(
                MutableInvalidationReason.CommittedStateFinalizationFailed);
        }

        touchedMutables.Clear();

        var cleanupFailures = new List<Exception>();
        CollectCleanupFailures(
            cleanupFailures,
            () => Provider.State.Cache.RemoveTransactionBestEffort(this));
        CollectCleanupFailures(
            cleanupFailures,
            Provider.State.Cache.ClearForRecovery);
        CollectCleanupFailures(
            cleanupFailures,
            Provider.State.Cache.DiscardRecoveryNotifications);

        throw new TransactionCommitFinalizationException(
            TransactionID,
            finalizationFailure,
            cleanupFailures);
    }

    private static void CollectCleanupFailures(
        List<Exception> failures,
        Func<IReadOnlyList<Exception>> cleanup)
    {
        try
        {
            failures.AddRange(cleanup());
        }
        catch (Exception cleanupFailure)
        {
            failures.Add(cleanupFailure);
        }
    }

    private IReadOnlyList<Exception> FinalizeCommitOutcomeUnknownState()
    {
        var recoveryFailures = new List<Exception>();

        MutableOwnership.MarkCommitOutcomeUnknown();
        foreach (var touchedMutable in touchedMutables)
        {
            try
            {
                touchedMutable.Invalidate(
                    MutableInvalidationReason.CommitOutcomeUnknown);
            }
            catch (Exception exception)
            {
                recoveryFailures.Add(exception);
            }
        }

        touchedMutables.Clear();
        CollectCleanupFailures(
            recoveryFailures,
            () => Provider.State.Cache.RemoveTransactionBestEffort(this));
        CollectCleanupFailures(
            recoveryFailures,
            Provider.State.Cache.ClearForRecovery);
        CollectCleanupFailures(
            recoveryFailures,
            Provider.State.Cache.DiscardRecoveryNotifications);
        return recoveryFailures;
    }

    private IReadOnlyList<Exception> FinalizeUncommittedState(
        MutableTransactionOutcome outcome,
        MutableInvalidationReason invalidationReason)
    {
        var cleanupFailures = new List<Exception>();

        try
        {
            MarkMutableOwnershipOutcome(outcome);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }

        foreach (var touchedMutable in touchedMutables)
        {
            try
            {
                touchedMutable.Invalidate(invalidationReason);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        touchedMutables.Clear();
        CollectCleanupFailures(
            cleanupFailures,
            () => Provider.State.Cache.RemoveTransactionBestEffort(this));
        return cleanupFailures;
    }

    private InvalidOperationException CreateRollbackOutcomeFailure(
        bool commitOutcomeUnknown,
        bool attachedRollbackWasAmbiguous,
        bool providerReportedRolledBack)
    {
        if (commitOutcomeUnknown)
        {
            return new InvalidOperationException(
                $"The provider rollback attempt returned for transaction {TransactionID}, but an earlier provider Commit() call failed before DataLinq could establish whether the database committed. " +
                "DataLinq invalidated transaction-derived state and cannot report a definite rollback; materialize fresh committed rows before continuing.");
        }

        if (attachedRollbackWasAmbiguous && providerReportedRolledBack)
        {
            return new InvalidOperationException(
                $"The attached transaction adapter reported rollback for transaction {TransactionID}, but DataLinq cannot establish whether the externally owned transaction was completed through or outside this wrapper. " +
                "Transaction-derived state was invalidated with an unknown rollback outcome; materialize fresh committed rows before continuing.");
        }

        return new InvalidOperationException(
            $"The provider rollback call returned for transaction {TransactionID} without reporting a rolled-back status. " +
            "DataLinq invalidated transaction-derived state with an unknown rollback outcome; materialize fresh committed rows before continuing.");
    }

    private bool IsAttachedRollbackOutcomeAmbiguous()
    {
        if (!isAttachedTransaction)
            return false;

        try
        {
            return DatabaseAccess.DbTransaction?.Connection?.State != ConnectionState.Open;
        }
        catch
        {
            // An externally owned transaction handle can be disposed or otherwise become
            // unreadable before the wrapper sees it. Treat that as external-completion
            // ambiguity instead of manufacturing a definite rollback result.
            return true;
        }
    }

    private void MarkMutableOwnershipOutcome(MutableTransactionOutcome outcome)
    {
        switch (outcome)
        {
            case MutableTransactionOutcome.RolledBack:
                MutableOwnership.MarkRolledBack();
                break;
            case MutableTransactionOutcome.RollbackOutcomeUnknown:
                MutableOwnership.MarkRollbackOutcomeUnknown();
                break;
            case MutableTransactionOutcome.OpenTransactionDisposed:
                MutableOwnership.MarkOpenTransactionDisposed();
                break;
            case MutableTransactionOutcome.CommitOutcomeUnknown:
                MutableOwnership.MarkCommitOutcomeUnknown();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(outcome),
                    outcome,
                    "The mutable ownership outcome is not an uncommitted terminal outcome.");
        }
    }

    private Exception? CaptureDeferredRolledBackStatusFailure()
    {
        try
        {
            PublishDeferredRolledBackStatus();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void ThrowManagedCompletionFailures(
        string operation,
        MutableInvalidationReason? invalidationReason,
        Exception? providerFailure,
        IReadOnlyList<Exception> cleanupFailures,
        Exception? observerFailure)
    {
        Exception? primaryFailure = providerFailure;
        var secondaryFailures = new List<Exception>();

        foreach (var cleanupFailure in cleanupFailures)
        {
            if (primaryFailure is null)
                primaryFailure = cleanupFailure;
            else
                secondaryFailures.Add(cleanupFailure);
        }

        if (observerFailure is not null)
        {
            if (primaryFailure is null)
                primaryFailure = observerFailure;
            else
                secondaryFailures.Add(observerFailure);
        }

        if (primaryFailure is null)
            return;

        AddManagedCompletionFailureContext(
            primaryFailure,
            operation,
            invalidationReason,
            secondaryFailures);
        ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }

    private void AddManagedCompletionFailureContext(
        Exception primaryFailure,
        string operation,
        MutableInvalidationReason? invalidationReason,
        IReadOnlyList<Exception> secondaryFailures)
    {
        try
        {
            primaryFailure.Data["DataLinq.TransactionId"] = TransactionID;
            primaryFailure.Data["DataLinq.CompletionOperation"] = operation;
            primaryFailure.Data["DataLinq.LocalFinalizationAttempted"] = true;
            if (invalidationReason is not null)
            {
                primaryFailure.Data["DataLinq.MutableInvalidationReason"] =
                    invalidationReason.Value.ToString();
            }

            if (secondaryFailures.Count > 0)
            {
                primaryFailure.Data["DataLinq.SecondaryCompletionFailures"] =
                    Array.AsReadOnly(secondaryFailures.ToArray());
            }
        }
        catch
        {
            // Exception context is best-effort. It must never replace the exact provider,
            // cleanup, or observer exception selected by the completion failure policy.
        }
    }

    private void HandleDatabaseStatusChanged(
        object? sender,
        DatabaseTransactionStatusChangeEventArgs args)
    {
        if (args.Status == DatabaseTransactionStatus.Committed &&
            Volatile.Read(ref managedCommitFinalizationState) != 0)
        {
            Volatile.Write(ref deferredCommittedStatus, 1);
            return;
        }

        if (args.Status == DatabaseTransactionStatus.RolledBack &&
            Volatile.Read(ref managedRollbackFinalizationState) != 0)
        {
            Volatile.Write(ref deferredRolledBackStatus, 1);
            return;
        }

        OnStatusChanged?.Invoke(
            this,
            new TransactionStatusChangeEventArgs(this, args.Status));
    }

    private void PublishDeferredCommittedStatus()
    {
        if (Interlocked.Exchange(ref deferredCommittedStatus, 0) == 0)
            return;

        OnStatusChanged?.Invoke(
            this,
            new TransactionStatusChangeEventArgs(
                this,
                DatabaseTransactionStatus.Committed));
    }

    private void PublishDeferredRolledBackStatus()
    {
        if (Interlocked.Exchange(ref deferredRolledBackStatus, 0) == 0)
            return;

        OnStatusChanged?.Invoke(
            this,
            new TransactionStatusChangeEventArgs(
                this,
                DatabaseTransactionStatus.RolledBack));
    }

    internal void EnsureMutationNotPoisoned(TransactionChangeType operation)
    {
        ThrowIfOperationInProgress($"execute {operation.ToString().ToLowerInvariant()}");
        ThrowIfCommitOutcomeUnknown(
            $"execute {operation.ToString().ToLowerInvariant()}",
            allowRollback: false);
        ThrowIfRollbackAttemptFailed($"execute {operation.ToString().ToLowerInvariant()}");
        ThrowIfPoisoned($"execute {operation.ToString().ToLowerInvariant()}");
    }

    internal void EnsureMutationCommitOutcomeKnown(TransactionChangeType operation) =>
        ThrowIfCommitOutcomeUnknown(
            $"execute {operation.ToString().ToLowerInvariant()}",
            allowRollback: false);

    internal void EnsureCanRead(string operation)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(Transaction));

        ThrowIfCommitOutcomeUnknown(operation, allowRollback: false);

        if (Status == DatabaseTransactionStatus.Committed)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} through transaction {TransactionID} because it is already committed.");
        }

        if (Status == DatabaseTransactionStatus.RolledBack)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} through transaction {TransactionID} because it is already rolled back.");
        }

        ThrowIfRollbackAttemptFailed(operation);

        if (Volatile.Read(ref internalReadThreadId) != Environment.CurrentManagedThreadId)
            ThrowIfOperationInProgress(operation);

        ThrowIfPoisoned(operation);
    }

    internal void EnsureTerminalReadSourceFallbackAllowed(string operation)
    {
        ThrowIfCommitOutcomeUnknown(operation, allowRollback: false);

        if (MutableOwnership.Outcome is
            MutableTransactionOutcome.RollbackOutcomeUnknown or
            MutableTransactionOutcome.CommittedStateFinalizationFailed)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} through transaction {TransactionID} because its terminal database outcome or committed local state is not trustworthy. " +
                "Materialize a fresh committed row before continuing.");
        }

        if (Status == DatabaseTransactionStatus.Committed &&
            Volatile.Read(ref managedCommitFinalizationState) == 1)
        {
            ThrowIfOperationInProgress(operation);
        }
    }

    internal void EnsureMutationPreflight(
        IModelInstance model,
        TransactionChangeType operation) =>
        MutationPreflight.Ensure(this, model, operation);

    internal void EnsureMutationPreflight(StateChange change) =>
        MutationPreflight.Ensure(this, change);

    internal void EnsureMutationPreflight(
        TransactionChangeType operation,
        Type modelType) =>
        MutationPreflight.EnsureTransactionAllowsWrite(this, operation, modelType);

    private void EnsureTransactionCanComplete(
        string operation,
        bool rejectPoisoned)
    {
        if (Volatile.Read(ref disposeState) != 0)
            throw new ObjectDisposedException(nameof(Transaction));

        ThrowIfCommitOutcomeUnknown(
            operation,
            allowRollback: !rejectPoisoned);

        if (Status == DatabaseTransactionStatus.Committed)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} transaction {TransactionID} because it is already committed.");
        }

        if (Status == DatabaseTransactionStatus.RolledBack)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} transaction {TransactionID} because it is already rolled back.");
        }

        ThrowIfRollbackAttemptFailed(operation);

        if (rejectPoisoned)
        {
            ThrowIfPoisoned(operation);
        }
    }

    private void BeginExclusiveOperation(string operation)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(Transaction));

        if (Interlocked.CompareExchange(ref exclusiveOperationState, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} through transaction {TransactionID} while another managed transaction operation is being finalized.");
        }

        if (IsDisposed)
        {
            EndExclusiveOperation();
            throw new ObjectDisposedException(nameof(Transaction));
        }
    }

    private void EndExclusiveOperation() =>
        Volatile.Write(ref exclusiveOperationState, 0);

    private void ThrowIfOperationInProgress(string operation)
    {
        if (Volatile.Read(ref exclusiveOperationState) == 0)
            return;

        throw new InvalidOperationException(
            $"Cannot {operation} through transaction {TransactionID} while another managed transaction operation is being finalized.");
    }

    private void ThrowIfRollbackAttemptFailed(string operation)
    {
        if (Volatile.Read(ref managedRollbackAttempted) == 0 ||
            Status == DatabaseTransactionStatus.RolledBack)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Cannot {operation} through transaction {TransactionID} because the rollback outcome is unknown after a managed rollback attempt and the provider did not report a completed rollback. " +
            "Only Dispose() remains legal; materialize fresh committed rows before retrying through a new transaction.");
    }

    private void ThrowIfCommitOutcomeUnknown(
        string operation,
        bool allowRollback)
    {
        if (MutableOwnership.Outcome != MutableTransactionOutcome.CommitOutcomeUnknown)
            return;

        var providerStatus = Status;
        var providerStatusIsTerminal =
            providerStatus == DatabaseTransactionStatus.Committed ||
            providerStatus == DatabaseTransactionStatus.RolledBack;
        if (allowRollback && !providerStatusIsTerminal)
            return;

        var allowedRecovery = providerStatusIsTerminal
            ? $"The provider already reports {providerStatus}; only Dispose() remains legal through the managed wrapper."
            : "Only Rollback() or Dispose() remains legal through the managed wrapper.";

        throw new InvalidOperationException(
            $"Cannot {operation} through transaction {TransactionID} because its provider commit call failed before DataLinq could establish the database outcome. " +
            $"{allowedRecovery} Materialize fresh committed rows before retrying through a new transaction.");
    }

    private void BeginInternalRead()
    {
        var threadId = Environment.CurrentManagedThreadId;
        if (Interlocked.CompareExchange(ref internalReadThreadId, threadId, 0) != 0)
        {
            throw new InvalidOperationException(
                $"Transaction {TransactionID} cannot start a second authoritative-row read while finalizing a mutation.");
        }
    }

    private void EndInternalRead() =>
        Volatile.Write(ref internalReadThreadId, 0);

    private void ThrowIfPoisoned(string operation)
    {
        var transactionFailure = Failure;
        if (transactionFailure is null)
            return;

        var failureDescription = transactionFailure.Stage switch
        {
            TransactionFailureStage.ProviderStatement => "provider statement preparation or execution",
            TransactionFailureStage.Hydration => "generated-value or authoritative-row hydration",
            TransactionFailureStage.PendingCacheApplication => "transaction-local cache application",
            TransactionFailureStage.LifecycleFinalization => "mutable lifecycle finalization or successful-change recording",
            _ => "mutation finalization"
        };

        throw new TransactionPoisonedException(
            $"DataLinq rejected the attempt to {operation} because transaction {TransactionID} is poisoned after a failure during {failureDescription}. " +
            "DataLinq-managed reads, writes, and commit are blocked. Call Rollback() or Dispose(), then materialize fresh committed rows before retrying. " +
            "Low-level DatabaseAccess and underlying IDbTransaction handles are outside this managed guard.");
    }

    /// <summary>
    /// Disposes of the transaction.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        BeginExclusiveOperation("dispose");
        try
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
                return;

            var ownershipOutcome = MutableOwnership.Outcome;
            var commitOutcomeUnknown =
                ownershipOutcome == MutableTransactionOutcome.CommitOutcomeUnknown;
            var finalizeOpenTransaction = commitOutcomeUnknown ||
                (ownershipOutcome == MutableTransactionOutcome.Unresolved &&
                 Status != DatabaseTransactionStatus.Committed &&
                 Status != DatabaseTransactionStatus.RolledBack);
            Volatile.Write(ref managedRollbackFinalizationState, 1);
            try
            {
                Exception? providerFailure = null;
                try
                {
                    DatabaseAccess.Dispose();
                }
                catch (Exception exception)
                {
                    providerFailure = exception;
                }

                IReadOnlyList<Exception> cleanupFailures;
                MutableInvalidationReason? invalidationReason =
                    MutableOwnership.InvalidationReason;
                if (finalizeOpenTransaction)
                {
                    var terminalOutcome = commitOutcomeUnknown
                        ? MutableTransactionOutcome.CommitOutcomeUnknown
                        : MutableTransactionOutcome.OpenTransactionDisposed;
                    invalidationReason = commitOutcomeUnknown
                        ? MutableInvalidationReason.CommitOutcomeUnknown
                        : MutableInvalidationReason.OpenTransactionDisposed;
                    cleanupFailures = FinalizeUncommittedState(
                        terminalOutcome,
                        invalidationReason.Value);
                }
                else
                {
                    var failures = new List<Exception>();
                    CollectCleanupFailures(
                        failures,
                        () => Provider.State.Cache.RemoveTransactionBestEffort(this));
                    cleanupFailures = failures;
                }

                Volatile.Write(ref managedRollbackFinalizationState, 2);
                var observerFailure = CaptureDeferredRolledBackStatusFailure();
                ThrowManagedCompletionFailures(
                    operation: "Dispose",
                    invalidationReason,
                    providerFailure,
                    cleanupFailures,
                    observerFailure);
            }
            finally
            {
                Volatile.Write(ref deferredRolledBackStatus, 0);
                Volatile.Write(ref managedRollbackFinalizationState, 0);
            }
        }
        finally
        {
            EndExclusiveOperation();
        }
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="other">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public bool Equals(Transaction? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return TransactionID.Equals(other.TransactionID);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
            return false;
        if (ReferenceEquals(this, obj))
            return true;
        if (obj.GetType() != typeof(Transaction))
            return false;

        return TransactionID.Equals(((Transaction)obj).TransactionID);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode()
    {
        return TransactionID.GetHashCode();
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return $"Transaction with ID '{TransactionID}': {Type}";
    }
}

/// <summary>
/// Represents a database transaction.
/// </summary>
/// <typeparam name="T">The type of the database model.</typeparam>
public class Transaction<T> : Transaction, IDataSourceAccess<T>
    where T : class, IDatabaseModel<T>
{
    /// <summary>
    /// Gets the database for the transaction.
    /// </summary>
    protected T Database { get; }

    IDatabaseProvider<T> IDataSourceAccess<T>.Provider => Provider as IDatabaseProvider<T> ?? throw new InvalidCastException("Provider is not of type IDatabaseProvider<T>");

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction{T}"/> class.
    /// </summary>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="type">The type of the transaction.</param>
    public Transaction(IDatabaseProvider<T> databaseProvider, TransactionType type) : base(databaseProvider, type)
    {
        Database = InstanceFactory.NewDatabase<T>(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction{T}"/> class.
    /// </summary>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="dbTransaction">The database transaction.</param>
    /// <param name="type">The type of the transaction.</param>
    public Transaction(IDatabaseProvider<T> databaseProvider, IDbTransaction dbTransaction, TransactionType type) : base(databaseProvider, dbTransaction, type)
    {
        Database = InstanceFactory.NewDatabase<T>(this);
    }

    /// <summary>
    /// Gets the schema.
    /// </summary>
    /// <returns>The schema.</returns>
    public T Query()
    {
        EnsureCanRead("access the transaction query root");
        return Database;
    }

    /// <summary>
    /// Retrieves a model from the database using the specified provider key.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <param name="key">The provider-key components that identify the model.</param>
    /// <returns>The model if found; otherwise, <c>null</c>.</returns>
    public M? Get<M>(DataLinqKey key) where M : IImmutableInstance
    {
        EnsureCanRead("read a model by primary key");
        if (!Provider.Metadata.TryGetTableModel(typeof(M), out var tableModel))
            throw new Exception($"Found no TableDefinition for model '{typeof(M)}'");

        return (M?)Provider.GetTableCache(tableModel.Table).GetRow(key, this);
    }

    /// <summary>
    /// Retrieves a model from the database using the specified provider key.
    /// </summary>
    /// <typeparam name="M">The type of the model.</typeparam>
    /// <typeparam name="TKey">The provider-key type used by the table cache.</typeparam>
    /// <param name="key">The provider key that identifies the model.</param>
    /// <returns>The model if found; otherwise, <c>null</c>.</returns>
    internal M? Get<M, TKey>(TKey key)
        where M : IImmutableInstance
        where TKey : notnull
    {
        EnsureCanRead("read a model by provider key");
        return IImmutable<M>.GetByProviderKey(key, this);
    }

    /// <summary>
    /// Creates a new SQL query from the specified table name.
    /// </summary>
    /// <param name="tableName">The name of the table.</param>
    /// <returns>The SQL query.</returns>
    public SqlQuery From(string tableName)
    {
        EnsureCanRead("create a transaction query");
        var table = Provider.Metadata.GetTableModel(tableName).Table;

        return new SqlQuery(table, this);
    }

    /// <summary>
    /// Creates a new SQL query from the specified table metadata.
    /// </summary>
    /// <param name="table">The table metadata.</param>
    /// <returns>The SQL query.</returns>
    public SqlQuery From(TableDefinition table)
    {
        EnsureCanRead("create a transaction query");
        return new SqlQuery(table, this);
    }

    /// <summary>
    /// Creates a new SQL query from the specified model type.
    /// </summary>
    /// <typeparam name="V">The type of the model.</typeparam>
    /// <returns>The SQL query.</returns>
    public SqlQuery<V> From<V>() where V : IModel
    {
        EnsureCanRead("create a transaction query");
        return new SqlQuery<V>(this);
    }
}
