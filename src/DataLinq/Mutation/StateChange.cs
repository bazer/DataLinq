using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Mutation;

internal readonly record struct MutationWriteSlot(
    ColumnDefinition Column,
    bool IsAssigned,
    object? ModelValue);

internal enum StateChangeExecutionPhase
{
    ProviderStatement,
    Hydration,
    Completed
}

/// <summary>
/// Represents a change of state to be applied to a model within a transaction.
/// </summary>
public class StateChange
{
    private readonly IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> changes;
    private readonly IReadOnlyList<MutationWriteSlot> insertWriteSlots;
    private readonly Dictionary<ColumnDefinition, object?> originalValues = new();
    private readonly bool hasReloadableIdentityMappedPrimaryKey;
    private readonly long? capturedMutationVersion;
    private Dictionary<ColumnIndex, DataLinqKey>? finalizedRelationKeys;
    private IReadOnlyList<KeyValuePair<ColumnDefinition, object?>>? finalizedModelChanges;
    private long? finalizedMutationVersion;
    private StateChangeExecutionPhase executionPhase;
    private int executionState;
    internal StateChangeExecutionPhase ExecutionPhase => executionPhase;
    internal bool HasExecutionAttempted => Volatile.Read(ref executionState) != 0;

    /// <summary>
    /// Gets the type of change that will be applied to the model.
    /// </summary>
    public TransactionChangeType Type { get; }

    /// <summary>
    /// Gets the model that the change will be applied to.
    /// </summary>
    public IModelInstance Model { get; }

    /// <summary>
    /// Gets the table metadata associated with the model.
    /// </summary>
    public TableDefinition Table { get; }

    /// <summary>
    /// Gets the canonical primary key captured for the mutation. A successful auto-increment
    /// insert replaces an initially null key with the generated key.
    /// </summary>
    public DataLinqKey PrimaryKeys { get; private set; }

    /// <summary>
    /// Determines if the model has an auto-incrementing primary key.
    /// </summary>
    public bool HasAutoIncrement => Table.HasAutoIncrementPrimaryKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateChange"/> class.
    /// </summary>
    /// <param name="model">The model to apply the change to.</param>
    /// <param name="table">The table metadata for the model.</param>
    /// <param name="type">The type of change to be applied.</param>
    public StateChange(IModelInstance model, TableDefinition table, TransactionChangeType type)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(table);

        if (!ReferenceEquals(table, model.Metadata().Table))
        {
            throw new ArgumentException(
                "The state-change table must be the model's exact mapped table definition.",
                nameof(table));
        }

        if (table.Type == TableType.View)
            throw new InvalidOperationException("Cannot change a view.");

        if (type == TransactionChangeType.Update && model is not IMutableInstance)
            throw new InvalidOperationException("Cannot update a model that is not mutable.");

        if (type == TransactionChangeType.Insert && model is not IMutableInstance)
            throw new InvalidOperationException("Cannot insert a model that is not mutable.");

        if (model is IMutableInstance mutable)
        {
            if (type == TransactionChangeType.Delete && mutable.IsNew())
                throw new InvalidOperationException("Cannot delete a new model.");

            if (mutable.IsDeleted())
                throw new InvalidOperationException("Cannot change a deleted model.");
        }


        Model = model;
        Table = table;
        Type = type;
        capturedMutationVersion = model is IMutableChangeTracking trackedMutable
            ? trackedMutable.MutationVersion
            : null;

        PrimaryKeys = KeyFactory.GetKey(model, table.PrimaryKeyColumns);
        changes = CaptureChanges(model);
        insertWriteSlots = type == TransactionChangeType.Insert
            ? CaptureInsertWriteSlots(model, table, changes)
            : [];
        hasReloadableIdentityMappedPrimaryKey = type == TransactionChangeType.Insert &&
            HasReloadableIdentityMappedPrimaryKey(model, table);
        CaptureOriginalValues(model);
    }

    /// <summary>
    /// Enumerates the captured mutation assignments. Array values are returned as detached copies.
    /// </summary>
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges()
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            yield return new KeyValuePair<ColumnDefinition, object?>(
                change.Key,
                SnapshotMutationValue(change.Value));
        }
    }

    internal bool TryGetOriginalValue(ColumnDefinition column, out object? value) =>
        originalValues.TryGetValue(column, out value);

    internal IReadOnlyList<MutationWriteSlot> GetInsertWriteSlots() =>
        insertWriteSlots;

    internal bool HasSameCanonicalPrimaryKeyIdentity() =>
        PrimaryKeys.Equals(KeyFactory.GetKey(Model, Table.PrimaryKeyColumns));

    internal bool HasSameCapturedMutation()
        => HasSameMutation(changes, capturedMutationVersion);

    internal bool HasSameFinalizedMutation() =>
        finalizedModelChanges is not null &&
        HasSameMutation(finalizedModelChanges, finalizedMutationVersion);

    private bool HasSameMutation(
        IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> expectedChanges,
        long? expectedVersion)
    {
        if (expectedVersion is long version &&
            (Model is not IMutableChangeTracking trackedMutable ||
             trackedMutable.MutationVersion != version))
        {
            return false;
        }

        if (Model is not IMutableInstance mutable)
            return true;

        var currentChanges = mutable.GetChanges().ToArray();
        if (currentChanges.Length != expectedChanges.Count)
            return false;

        for (var capturedIndex = 0; capturedIndex < expectedChanges.Count; capturedIndex++)
        {
            var captured = expectedChanges[capturedIndex];
            var found = false;
            for (var currentIndex = 0; currentIndex < currentChanges.Length; currentIndex++)
            {
                var current = currentChanges[currentIndex];
                if (!ReferenceEquals(captured.Key, current.Key))
                    continue;

                if (!MutationValuesEqual(captured.Value, current.Value))
                    return false;

                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    internal DataLinqKey GetCurrentRelationKey(ColumnIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (finalizedRelationKeys?.TryGetValue(index, out var key) == true)
            return key;

        return KeyFactory.GetKey(Model, index.Columns);
    }

    internal void FinalizeSuccessfulRelationKeys(IImmutableInstance? authoritative)
    {
        if (authoritative is null)
            return;

        finalizedRelationKeys = CaptureRelationKeys(authoritative);
    }

    private static IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> CaptureChanges(IModelInstance model) =>
        model is IMutableInstance mutable
            ? mutable.GetChanges()
                .Select(static change => new KeyValuePair<ColumnDefinition, object?>(
                    change.Key,
                    SnapshotMutationValue(change.Value)))
                .ToArray()
            : [];

    private static object? SnapshotMutationValue(object? value) =>
        value is Array array
            ? array.Clone()
            : value;

    private static bool MutationValuesEqual(object? captured, object? current)
    {
        if (captured is Array || current is Array)
            return StructuralComparisons.StructuralEqualityComparer.Equals(captured, current);

        return Equals(captured, current);
    }

    private static bool HasReloadableIdentityMappedPrimaryKey(
        IModelInstance model,
        TableDefinition table)
    {
        if (table.PrimaryKeyColumns.Count == 0 ||
            table.PrimaryKeyColumns.Any(static column => column.HasScalarConverter))
        {
            return false;
        }

        if (table.PrimaryKeyColumns.All(column => model[column] is not null))
            return true;

        return table.PrimaryKeyColumns.Count == 1 &&
            table.AutoIncrementPrimaryKeyColumn is { } autoIncrementPrimaryKey &&
            GeneratedValueDecoder.CanDecodeAutoIncrementValue(autoIncrementPrimaryKey);
    }

    private static IReadOnlyList<MutationWriteSlot> CaptureInsertWriteSlots(
        IModelInstance model,
        TableDefinition table,
        IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> changes)
    {
        var assignedValues = new Dictionary<ColumnDefinition, object?>(changes.Count);
        for (var index = 0; index < changes.Count; index++)
            assignedValues[changes[index].Key] = changes[index].Value;

        var slots = new MutationWriteSlot[table.ColumnCount];
        for (var index = 0; index < table.ColumnCount; index++)
        {
            var column = table.Columns[index];
            var isAssigned = assignedValues.TryGetValue(column, out var assignedValue);
            slots[index] = new MutationWriteSlot(
                column,
                isAssigned,
                SnapshotMutationValue(isAssigned ? assignedValue : model[column]));
        }

        return slots;
    }

    private void CaptureOriginalValues(IModelInstance model)
    {
        if (model.GetRowData() is not MutableRowData rowData)
            return;

        var columns = new HashSet<ColumnDefinition>();
        if (Type == TransactionChangeType.Delete)
        {
            foreach (var index in Table.ColumnIndices)
                columns.UnionWith(index.Columns);
        }
        else if (Type == TransactionChangeType.Update)
        {
            foreach (var change in changes)
            {
                foreach (var index in Table.GetColumnIndices(change.Key))
                    columns.UnionWith(index.Columns);
            }
        }

        foreach (var column in columns)
        {
            if (rowData.TryGetOriginalValue(column, out var value))
                originalValues[column] = SnapshotMutationValue(value);
        }
    }

    /// <summary>
    /// Executes the state change through the transaction-owned mutation pipeline, including
    /// transaction-local cache application, authoritative hydration, lifecycle finalization,
    /// and successful-change recording. A state change is single-attempt once provider execution
    /// begins; create a new candidate from a trustworthy mutable baseline before retrying.
    /// </summary>
    /// <param name="transaction">The transaction to execute the query on.</param>
    public void ExecuteQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        _ = transaction.ExecuteStateChange(this);
    }

    internal void ExecutePreflightedQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!TryBeginExecution())
        {
            throw new InvalidOperationException(
                "This state change has already started provider execution and cannot be executed again.");
        }

        ExecuteReservedQuery(transaction);
    }

    internal bool TryBeginExecution() =>
        Interlocked.CompareExchange(ref executionState, 1, 0) == 0;

    internal void ExecuteReservedQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ExecuteQueryCore(transaction);
    }

    private void ExecuteQueryCore(Transaction transaction)
    {
        executionPhase = StateChangeExecutionPhase.ProviderStatement;
        var telemetryContext = DataLinqTelemetryContext.FromProvider(transaction.Provider);
        var activity = DataLinqTelemetry.StartMutationActivity(telemetryContext, Table.DbName, Type, transaction.Type);
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;
        var affectedRows = 0;

        try
        {
            var command = GetDbCommandCore(transaction);
            EnsureCapturedMutationUnchanged("provider command preparation");

            if (Type == TransactionChangeType.Insert && HasAutoIncrement && PrimaryKeys.IsNull)
            {
                var newId = transaction.DatabaseAccess.ExecuteScalar(command);
                affectedRows = 1;
                EnsureCapturedMutationUnchanged("provider statement execution");
                executionPhase = StateChangeExecutionPhase.Hydration;

                if (Model is IMutableInstance mutable)
                {
                    var autoIncrement = Table.AutoIncrementPrimaryKeyColumn;

                    if (autoIncrement != null)
                    {
                        var canonicalValue = GeneratedValueDecoder.DecodeAutoIncrementValue(
                            autoIncrement,
                            newId,
                            "sql.generated");
                        var modelValue = ProviderRowMaterializer.MaterializeValue(
                            autoIncrement,
                            canonicalValue,
                            "sql.generated");
                        mutable[autoIncrement] = modelValue;
                    }
                }
            }
            else
            {
                affectedRows = transaction.DatabaseAccess.ExecuteNonQuery(command);
                EnsureCapturedMutationUnchanged("provider statement execution");
            }

            executionPhase = StateChangeExecutionPhase.Hydration;
            FinalizePrimaryKeysAfterExecution();
            FinalizeRelationKeysAfterExecution();
            CaptureFinalizedMutation();
            executionPhase = StateChangeExecutionPhase.Completed;
            succeeded = true;
        }
        catch (Exception exception)
        {
            DataLinqTelemetry.RecordException(activity, exception);
            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordMutationExecution(
                telemetryContext,
                Table.DbName,
                Type,
                transaction.Type,
                succeeded,
                affectedRows,
                duration);

            if (activity is not null)
            {
                activity.SetTag("datalinq.outcome", succeeded ? "success" : "failure");
                activity.SetTag("db.operation.rows_affected", affectedRows);
                activity.Dispose();
            }
        }
    }

    private void EnsureCapturedMutationUnchanged(string stage)
    {
        if (HasSameCapturedMutation())
            return;

        throw new InvalidOperationException(
            $"The mutable assignments changed during {stage}.");
    }

    private void FinalizePrimaryKeysAfterExecution()
    {
        var currentPrimaryKeys = KeyFactory.GetKey(Model, Table.PrimaryKeyColumns);
        if (PrimaryKeys.Equals(currentPrimaryKeys))
        {
            if (Type == TransactionChangeType.Insert &&
                HasAutoIncrement &&
                PrimaryKeys.IsNull)
            {
                throw new InvalidOperationException(
                    "The auto-increment mutation completed without a canonical generated primary key.");
            }

            return;
        }

        if (Type == TransactionChangeType.Insert &&
            HasAutoIncrement &&
            PrimaryKeys.IsNull &&
            !currentPrimaryKeys.IsNull)
        {
            PrimaryKeys = currentPrimaryKeys;
            return;
        }

        throw new InvalidOperationException(
            "The model primary-key identity changed while the provider mutation was executing.");
    }

    private void FinalizeRelationKeysAfterExecution()
        => finalizedRelationKeys = CaptureRelationKeys(Model);

    private Dictionary<ColumnIndex, DataLinqKey> CaptureRelationKeys(
        IModelInstance model)
    {
        var relationKeys = new Dictionary<ColumnIndex, DataLinqKey>(Table.ColumnIndices.Count);
        foreach (var index in Table.ColumnIndices)
        {
            DataLinqKey key;
            if (Type == TransactionChangeType.Delete &&
                TryGetOriginalRelationKey(index, out var originalKey))
            {
                key = originalKey;
            }
            else
            {
                key = index.Columns.SequenceEqual(Table.PrimaryKeyColumns)
                    ? PrimaryKeys
                    : KeyFactory.GetKey(model, index.Columns);
            }

            relationKeys.Add(index, key);
        }

        return relationKeys;
    }

    private bool TryGetOriginalRelationKey(
        ColumnIndex index,
        out DataLinqKey key)
    {
        var values = new object?[index.Columns.Count];
        for (var columnIndex = 0; columnIndex < index.Columns.Count; columnIndex++)
        {
            if (!originalValues.TryGetValue(index.Columns[columnIndex], out var value))
            {
                key = DataLinqKey.Null;
                return false;
            }

            values[columnIndex] = value;
        }

        key = KeyFactory.CreateKeyFromModelValues(values, index.Columns);
        return true;
    }

    private void CaptureFinalizedMutation()
    {
        finalizedMutationVersion = Model is IMutableChangeTracking trackedMutable
            ? trackedMutable.MutationVersion
            : null;
        finalizedModelChanges = CaptureChanges(Model);
    }

    /// <summary>
    /// Creates a database command for the state change to be executed within the transaction.
    /// </summary>
    /// <param name="transaction">The transaction the command is for.</param>
    /// <returns>The database command to execute.</returns>
    public IDbCommand GetDbCommand(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.EnsureMutationPreflight(this);

        return GetDbCommandCore(transaction);
    }

    private IDbCommand GetDbCommandCore(Transaction transaction) =>
        transaction.Provider.ToDbCommand(GetQueryCore(transaction));

    /// <summary>
    /// Generates the query for the state change.
    /// </summary>
    /// <param name="transaction">The transaction the query is for.</param>
    /// <returns>The query representing the state change.</returns>
    public IQuery GetQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.EnsureMutationPreflight(this);

        return GetQueryCore(transaction);
    }

    private IQuery GetQueryCore(Transaction transaction)
    {
        var query = new SqlQuery(Table, transaction);
        var writer = transaction.Provider.GetWriter();

        return Type switch
        {
            TransactionChangeType.Insert => BuildInsertQuery(query, writer),
            TransactionChangeType.Update => BuildUpdateQuery(query, writer),
            TransactionChangeType.Delete => BuildDeleteQuery(query, writer),
            _ => throw new NotImplementedException("The transaction change type is not implemented."),
        };
    }

    private IQuery BuildInsertQuery(SqlQuery query, IDataLinqDataWriter writer)
    {
        var supportsDefaultOnlyInsert =
            query.DataSource.Provider.Constants.DefaultValuesInsertClause is not null;

        foreach (var slot in insertWriteSlots)
        {
            if (ShouldOmitUnsetAutoIncrementPrimaryKey(slot, supportsDefaultOnlyInsert) ||
                ShouldOmitUnsetServerDefault(slot, query.DataSource.Provider.DatabaseType))
                continue;

            var value = writer.ConvertModelColumnValue(
                slot.Column,
                slot.ModelValue,
                "mutation.insert");
            query.Set(slot.Column.DbName, value);
        }

        if (HasAutoIncrement)
            query.AddLastIdQuery();

        return query.InsertQuery();
    }

    private bool ShouldOmitUnsetAutoIncrementPrimaryKey(
        MutationWriteSlot slot,
        bool supportsDefaultOnlyInsert) =>
        supportsDefaultOnlyInsert &&
        !slot.IsAssigned &&
        slot.ModelValue is null &&
        hasReloadableIdentityMappedPrimaryKey &&
        ReferenceEquals(slot.Column, Table.AutoIncrementPrimaryKeyColumn);

    private bool ShouldOmitUnsetServerDefault(
        MutationWriteSlot slot,
        DatabaseType databaseType)
    {
        if (slot.IsAssigned ||
            slot.ModelValue is not null ||
            !hasReloadableIdentityMappedPrimaryKey ||
            slot.Column.PrimaryKey ||
            slot.Column.HasScalarConverter ||
            slot.Column.ColumnIndices.Any())
        {
            return false;
        }

        return slot.Column.ValueProperty.GetDefaultAttribute() is DefaultSqlAttribute defaultSql &&
            (defaultSql.DatabaseType == DatabaseType.Default || defaultSql.DatabaseType == databaseType);
    }

    private IQuery BuildUpdateQuery(SqlQuery query, IDataLinqDataWriter writer)
    {
        for (var index = 0; index < Table.PrimaryKeyColumns.Count; index++)
        {
            var column = Table.PrimaryKeyColumns[index];
            query.Where(column.DbName).EqualTo(
                writer.ConvertColumnValue(
                    column,
                    PrimaryKeys.GetValue(index)));
        }

        foreach (var change in changes)
            query.Set(
                change.Key.DbName,
                writer.ConvertModelColumnValue(change.Key, change.Value, "mutation.update.value"));

        return query.UpdateQuery();
    }

    private IQuery BuildDeleteQuery(SqlQuery query, IDataLinqDataWriter writer)
    {
        for (var index = 0; index < Table.PrimaryKeyColumns.Count; index++)
        {
            var column = Table.PrimaryKeyColumns[index];
            query.Where(column.DbName).EqualTo(
                writer.ConvertColumnValue(
                    column,
                    PrimaryKeys.GetValue(index)));
        }

        return query.DeleteQuery();
    }
}
