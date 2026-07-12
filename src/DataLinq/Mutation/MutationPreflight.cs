using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Mutation;

internal static class MutationPreflight
{
    internal static void Ensure(
        Transaction transaction,
        IModelInstance model,
        TransactionChangeType operation)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(model);
        EnsureTransactionAllowsWrite(
            transaction,
            operation,
            model,
            modelType: null);
        EnsureTargetProviderMapsModel(transaction, model, operation);

        if (model is IMutableLifecycle mutableLifecycle)
        {
            EnsureMutableLifecycleAllowsWrite(
                transaction,
                model,
                mutableLifecycle.Lifecycle,
                mutableLifecycle.BaselineCanonicalPrimaryKey,
                operation);
            return;
        }

        if (model is IMutableInstance legacyMutable)
        {
            EnsureLegacyMutableAllowsWrite(
                transaction,
                legacyMutable,
                operation);
            return;
        }

        if (operation == TransactionChangeType.Delete &&
            model is IImmutableInstance immutable)
        {
            EnsureImmutableDeleteOrigin(transaction, immutable);
        }
    }

    internal static void Ensure(
        Transaction transaction,
        StateChange change)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(change);

        Ensure(transaction, change.Model, change.Type);

        var capturedChanges = change.GetChanges().ToArray();
        EnsureChangesBelongToMappedTable(
            transaction,
            change.Model,
            capturedChanges,
            change.Type);

        if (change.Type != TransactionChangeType.Insert)
        {
            EnsurePrimaryKeyIsUnchanged(
                transaction,
                change.Model,
                capturedChanges,
                change.Type);
        }

        var hasSameIdentity = change.Type != TransactionChangeType.Insert &&
            change.Model is IMutableLifecycle mutableLifecycle
            ? change.PrimaryKeys.Equals(mutableLifecycle.BaselineCanonicalPrimaryKey)
            : change.HasSameCanonicalPrimaryKeyIdentity();
        if (!hasSameIdentity)
        {
            throw MutationRejected(
                transaction,
                change.Type,
                change.Model,
                modelType: null,
                "The model primary-key identity changed after this state change was captured.",
                "Create a new state change from the current committed baseline before retrying.");
        }

        if (!change.HasSameCapturedMutation())
        {
            throw MutationRejected(
                transaction,
                change.Type,
                change.Model,
                modelType: null,
                "The mutable assignments changed after this state change was captured.",
                "Create a fresh state change from the current mutable before retrying.");
        }
    }

    internal static void EnsureExecution(
        Transaction transaction,
        StateChange change)
    {
        Ensure(transaction, change);

        if (change.HasExecutionAttempted)
        {
            throw MutationRejected(
                transaction,
                change.Type,
                change.Model,
                modelType: null,
                "This state change has already started provider execution.",
                "Create a fresh state change from a trustworthy mutable baseline before retrying.");
        }
    }

    internal static void EnsureTransactionAllowsWrite(
        Transaction transaction,
        TransactionChangeType operation,
        Type modelType)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(modelType);
        EnsureTransactionAllowsWrite(
            transaction,
            operation,
            model: null,
            modelType);
    }

    private static void EnsureTransactionAllowsWrite(
        Transaction transaction,
        TransactionChangeType operation,
        IModelInstance? model,
        Type? modelType)
    {
        if (transaction.IsDisposed)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType,
                $"Transaction {transaction.TransactionID} has been disposed.",
                "Start a new transaction and materialize a fresh committed row before retrying the mutation.");
        }

        transaction.EnsureMutationCommitOutcomeKnown(operation);

        if (transaction.Status == DatabaseTransactionStatus.Committed)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType,
                $"Transaction {transaction.TransactionID} is already committed.",
                "Start a new transaction; if this mutable was touched by that transaction, materialize a fresh committed row before retrying.");
        }

        if (transaction.Status == DatabaseTransactionStatus.RolledBack)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType,
                $"Transaction {transaction.TransactionID} is already rolled back.",
                "Start a new transaction and materialize a fresh committed row before retrying the mutation.");
        }

        transaction.EnsureMutationNotPoisoned(operation);

        if (transaction.Type == TransactionType.ReadOnly)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType,
                $"Transaction {transaction.TransactionID} is read-only.",
                "Use a ReadAndWrite or WriteOnly transaction.");
        }
    }

    private static void EnsureMutableLifecycleAllowsWrite(
        Transaction transaction,
        IModelInstance model,
        MutableLifecycleSnapshot snapshot,
        DataLinqKey baselineCanonicalPrimaryKey,
        TransactionChangeType operation)
    {
        if (snapshot.BaselineKind == MutableBaselineKind.Invalid)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                $"The mutable baseline is invalid because {DescribeInvalidation(snapshot.InvalidationReason)}.",
                "Materialize a fresh committed row and create a new mutable; Reset() cannot make this instance writable.");
        }

        if (snapshot.RowKind == MutableRowKind.Deleted)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                "The mutable represents a deleted row.",
                "Do not reuse it; materialize a current row before another mutation.");
        }

        if (operation == TransactionChangeType.Insert)
        {
            if (snapshot.RowKind != MutableRowKind.New)
            {
                throw new ArgumentException(
                    "Model is not a new row, unable to insert.",
                    nameof(model));
            }

            if (snapshot.BaselineKind != MutableBaselineKind.NoneForNew ||
                snapshot.ProviderOwner is not null ||
                snapshot.TransactionOwner is not null ||
                snapshot.NeutralReadSourceOwner is not null)
            {
                throw MutationRejected(
                    transaction,
                    operation,
                    model,
                    modelType: null,
                    "The new mutable has inconsistent baseline ownership state.",
                    "Create a fresh mutable before inserting it.");
            }

            if (model is IMutableInstance insertMutable)
            {
                EnsureChangesBelongToMappedTable(
                    transaction,
                    model,
                    insertMutable.GetChanges().ToArray(),
                    operation);
            }

            return;
        }

        if (snapshot.RowKind == MutableRowKind.New ||
            snapshot.BaselineKind == MutableBaselineKind.NoneForNew)
        {
            if (operation == TransactionChangeType.Update)
            {
                throw new ArgumentException(
                    "Model is a new row, unable to update.",
                    nameof(model));
            }

            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                "Delete requires an existing row, but the mutable is new.",
                "Insert the mutable first, or delete an existing row materialized by this provider.");
        }

        if (snapshot.RowKind != MutableRowKind.Existing ||
            (snapshot.BaselineKind != MutableBaselineKind.Committed &&
             snapshot.BaselineKind != MutableBaselineKind.TransactionLocal))
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                "The mutable lifecycle state is not a writable existing baseline.",
                "Materialize a fresh committed row and create a new mutable before retrying.");
        }

        EnsureExactMutableOwner(transaction, model, snapshot, operation);

        if (model is IMutableInstance mutable)
        {
            var mutableChanges = mutable.GetChanges().ToArray();
            EnsureChangesBelongToMappedTable(
                transaction,
                model,
                mutableChanges,
                operation);
            EnsurePrimaryKeyIsUnchanged(
                transaction,
                mutable,
                mutableChanges,
                operation);
        }

        EnsureBaselinePrimaryKeyIdentity(
            transaction,
            model,
            baselineCanonicalPrimaryKey,
            operation);
    }

    private static void EnsureExactMutableOwner(
        Transaction transaction,
        IModelInstance model,
        MutableLifecycleSnapshot snapshot,
        TransactionChangeType operation)
    {
        if (snapshot.ProviderOwner is null)
        {
            var reason = snapshot.NeutralReadSourceOwner is null
                ? "The mutable baseline has no SQL provider owner."
                : $"The mutable baseline belongs to neutral read source type '{snapshot.NeutralReadSourceOwner.GetType().FullName}'.";
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                reason,
                "Materialize the row through the target provider before mutating it.");
        }

        if (!ReferenceEquals(snapshot.ProviderOwner, transaction.Provider))
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                $"The mutable baseline belongs to a different provider instance ({DescribeProvider(snapshot.ProviderOwner)}) than target transaction {transaction.TransactionID}.",
                "Use the originating provider, or materialize the row through the target provider before mutating it.");
        }

        if (snapshot.BaselineKind == MutableBaselineKind.Committed)
        {
            if (snapshot.TransactionOwner is not null)
            {
                throw MutationRejected(
                    transaction,
                    operation,
                    model,
                    modelType: null,
                    "The committed mutable baseline retains inconsistent transaction ownership.",
                    "Materialize a fresh committed row before retrying the mutation.");
            }

            return;
        }

        if (snapshot.TransactionOwner is null)
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                "The transaction-local mutable baseline has no owning transaction token.",
                "Materialize a fresh committed row before retrying the mutation.");
        }

        if (!ReferenceEquals(snapshot.TransactionOwner, transaction.MutableOwnership))
        {
            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                $"The mutable baseline belongs to unresolved transaction {snapshot.TransactionOwner.TransactionId}, not target transaction {transaction.TransactionID}.",
                "Continue through the owning open transaction; otherwise materialize a fresh committed row.");
        }
    }

    private static void EnsureLegacyMutableAllowsWrite(
        Transaction transaction,
        IMutableInstance mutable,
        TransactionChangeType operation)
    {
        if (mutable.IsDeleted())
        {
            throw MutationRejected(
                transaction,
                operation,
                mutable,
                modelType: null,
                "The mutable represents a deleted row.",
                "Do not reuse it; materialize a current row before another mutation.");
        }

        if (operation == TransactionChangeType.Insert && !mutable.IsNew())
        {
            throw new ArgumentException(
                "Model is not a new row, unable to insert.",
                nameof(mutable));
        }

        if (operation == TransactionChangeType.Update && mutable.IsNew())
        {
            throw new ArgumentException(
                "Model is a new row, unable to update.",
                nameof(mutable));
        }

        if (operation == TransactionChangeType.Delete && mutable.IsNew())
        {
            throw MutationRejected(
                transaction,
                operation,
                mutable,
                modelType: null,
                "Delete requires an existing row, but the mutable is new.",
                "Insert the mutable first, or delete an existing row materialized by this provider.");
        }

        // Legacy custom mutable implementations have no source contract from which exact
        // provider ownership can be recovered. Preserve that low-level StateChange compatibility
        // while enforcing its public row-shape contract, terminal deletion, and primary-key
        // safety. Generated DataLinq mutables take the exact-owner lifecycle path above.
        var changes = mutable.GetChanges().ToArray();
        EnsureChangesBelongToMappedTable(
            transaction,
            mutable,
            changes,
            operation);
        if (operation != TransactionChangeType.Insert)
        {
            EnsurePrimaryKeyIsUnchanged(
                transaction,
                mutable,
                changes,
                operation);
        }
    }

    private static void EnsureImmutableDeleteOrigin(
        Transaction transaction,
        IImmutableInstance immutable)
    {
        var origin = MutableBaselineOrigin.FromImmutable(immutable);
        var transactionOwner = origin.TransactionOwner;
        var providerOwner = origin.ProviderOwner;

        if (transactionOwner?.Outcome == MutableTransactionOutcome.Committed)
            transactionOwner = null;

        // Legacy custom immutable implementations may not expose a source at all. Preserve that
        // compatibility; exact ownership is enforced whenever a provider or neutral source exists.
        if (providerOwner is null &&
            transactionOwner is null &&
            origin.NeutralReadSourceOwner is null)
        {
            return;
        }

        if (providerOwner is null)
        {
            var sourceType = origin.NeutralReadSourceOwner?.GetType().FullName ?? "unknown";
            throw MutationRejected(
                transaction,
                TransactionChangeType.Delete,
                immutable,
                modelType: null,
                $"The immutable row belongs to neutral read source type '{sourceType}', not a SQL provider.",
                "Materialize the row through the target provider before deleting it.");
        }

        if (!ReferenceEquals(providerOwner, transaction.Provider))
        {
            throw MutationRejected(
                transaction,
                TransactionChangeType.Delete,
                immutable,
                modelType: null,
                $"The immutable row belongs to a different provider instance ({DescribeProvider(providerOwner)}) than target transaction {transaction.TransactionID}.",
                "Use the originating provider, or materialize the row through the target provider before deleting it.");
        }

        if (transactionOwner is not null &&
            !ReferenceEquals(transactionOwner, transaction.MutableOwnership))
        {
            throw MutationRejected(
                transaction,
                TransactionChangeType.Delete,
                immutable,
                modelType: null,
                $"The immutable row belongs to unresolved transaction {transactionOwner.TransactionId}, not target transaction {transaction.TransactionID}.",
                "Continue through the owning open transaction; otherwise materialize a fresh committed row.");
        }

        EnsureImmutablePrimaryKeyIdentity(transaction, immutable);
    }

    private static void EnsureImmutablePrimaryKeyIdentity(
        Transaction transaction,
        IImmutableInstance immutable)
    {
        var table = immutable.Metadata().Table;
        var currentCanonicalPrimaryKey = KeyFactory.GetKey(
            immutable,
            table.PrimaryKeyColumns);
        if (currentCanonicalPrimaryKey.Equals(immutable.PrimaryKeys()))
            return;

        throw MutationRejected(
            transaction,
            TransactionChangeType.Delete,
            immutable,
            modelType: null,
            "The immutable row's current primary-key values no longer match its authoritative identity.",
            "Materialize a fresh committed row before deleting it.");
    }

    private static void EnsurePrimaryKeyIsUnchanged(
        Transaction transaction,
        IModelInstance model,
        IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> changes,
        TransactionChangeType operation)
    {
        var primaryKeyColumns = model.Metadata().Table.PrimaryKeyColumns;
        var changedPrimaryKeys = changes
            .Select(static change => change.Key)
            .Where(primaryKeyColumns.Contains)
            .Select(static column => column.DbName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static columnName => columnName, StringComparer.Ordinal)
            .ToArray();

        if (changedPrimaryKeys.Length == 0)
            return;

        throw MutationRejected(
            transaction,
            operation,
            model,
            modelType: null,
            $"Primary-key column(s) '{string.Join("', '", changedPrimaryKeys)}' have tracked assignments.",
            "Primary-key mutation is not supported; call Reset() to restore the baseline, or delete and insert explicitly.");
    }

    private static void EnsureChangesBelongToMappedTable(
        Transaction transaction,
        IModelInstance model,
        IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> changes,
        TransactionChangeType operation)
    {
        var table = model.Metadata().Table;
        for (var index = 0; index < changes.Count; index++)
        {
            var column = changes[index].Key;
            if (column is not null &&
                column.Index >= 0 &&
                column.Index < table.ColumnCount &&
                ReferenceEquals(table.Columns[column.Index], column))
            {
                continue;
            }

            throw MutationRejected(
                transaction,
                operation,
                model,
                modelType: null,
                $"Tracked assignment column '{column?.DbName ?? "unknown"}' is not an exact mapped column for table '{table.DbName}'.",
                "Discard the assignment and mutate only columns declared by this model's table metadata.");
        }
    }

    private static void EnsureTargetProviderMapsModel(
        Transaction transaction,
        IModelInstance model,
        TransactionChangeType operation)
    {
        var metadata = model.Metadata();
        if (ReferenceEquals(metadata.Database, transaction.Provider.Metadata))
            return;

        throw MutationRejected(
            transaction,
            operation,
            model,
            modelType: null,
            "The model's exact table metadata is not mapped by the target provider.",
            "Use a transaction from the model's database mapping or create the mutable from the target provider's generated model.");
    }

    private static void EnsureBaselinePrimaryKeyIdentity(
        Transaction transaction,
        IModelInstance model,
        DataLinqKey baselineCanonicalPrimaryKey,
        TransactionChangeType operation)
    {
        var currentCanonicalPrimaryKey = KeyFactory.GetKey(
            model,
            model.Metadata().Table.PrimaryKeyColumns);

        if (currentCanonicalPrimaryKey.Equals(baselineCanonicalPrimaryKey))
            return;

        throw MutationRejected(
            transaction,
            operation,
            model,
            modelType: null,
            "The mutable's current primary-key values no longer match its authoritative baseline.",
            "Discard this mutable and materialize a fresh committed row before retrying.");
    }

    private static MutationGuardException MutationRejected(
        Transaction transaction,
        TransactionChangeType operation,
        IModelInstance? model,
        Type? modelType,
        string reason,
        string recovery)
    {
        var (modelName, tableName) = DescribeModel(
            transaction,
            model,
            modelType);
        var providerType = transaction.Provider.GetType().FullName ??
            transaction.Provider.GetType().Name;
        var message =
            $"DataLinq rejected {operation.ToString().ToLowerInvariant()} for model '{modelName}' mapped to table '{tableName}' before provider command execution. " +
            $"Target transaction {transaction.TransactionID} is {transaction.Type} with provider status {transaction.Status} on {transaction.Provider.DatabaseType} provider type '{providerType}'. " +
            $"{reason} {recovery}";

        return new MutationGuardException(message);
    }

    private static (string ModelName, string TableName) DescribeModel(
        Transaction transaction,
        IModelInstance? model,
        Type? modelType)
    {
        if (model is not null)
        {
            var metadata = model.Metadata();
            return (DescribeModelType(metadata.CsType), metadata.Table.DbName);
        }

        if (modelType is not null &&
            transaction.Provider.Metadata.TryGetTableModel(modelType, out var tableModel))
        {
            return (
                DescribeModelType(tableModel.Model.CsType),
                tableModel.Table.DbName);
        }

        return (modelType?.FullName ?? "unknown", "unknown");
    }

    private static string DescribeModelType(CsTypeDeclaration modelType)
    {
        if (modelType.Type?.FullName is string runtimeName)
            return runtimeName;

        return string.IsNullOrWhiteSpace(modelType.Namespace)
            ? modelType.Name
            : $"{modelType.Namespace}.{modelType.Name}";
    }

    private static string DescribeProvider(IDatabaseProvider provider)
    {
        var providerType = provider.GetType().FullName ?? provider.GetType().Name;
        return $"{provider.DatabaseType} provider type '{providerType}'";
    }

    private static string DescribeInvalidation(MutableInvalidationReason? reason) =>
        reason switch
        {
            MutableInvalidationReason.RolledBack => "its transaction was rolled back",
            MutableInvalidationReason.RollbackOutcomeUnknown => "its transaction rollback outcome is unknown",
            MutableInvalidationReason.OpenTransactionDisposed => "its open transaction was disposed",
            MutableInvalidationReason.MutationFailed => "a mutation failed",
            MutableInvalidationReason.CommitOutcomeUnknown => "the commit outcome is unknown",
            MutableInvalidationReason.CommittedStateFinalizationFailed => "the database committed but local state finalization failed",
            _ => "the lifecycle origin is unavailable"
        };
}
