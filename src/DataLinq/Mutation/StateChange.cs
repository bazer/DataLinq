using System;
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
    private readonly MutationSnapshot snapshot;
    private readonly IReadOnlyList<ColumnIndex> affectedIndices;
    private readonly bool hasReloadablePrimaryKey;
    private readonly bool hasIntegralCanonicalPrimaryKeyShape;
    private object?[]? originalValues;
    private ulong originalValueOccupancy;
    private ulong[]? overflowOriginalValueOccupancy;
    private Dictionary<ColumnIndex, DataLinqKey>? finalizedRelationKeys;
    private MutationSnapshot? finalizedLegacySnapshot;
    private long? finalizedMutationVersion;
    private StateChangeExecutionPhase executionPhase;
    private int executionState;
    internal StateChangeExecutionPhase ExecutionPhase => executionPhase;
    internal bool HasExecutionAttempted => Volatile.Read(ref executionState) != 0;
    internal MutationSnapshot Snapshot => snapshot;
    internal IReadOnlyList<ColumnIndex> AffectedIndices => affectedIndices;
    internal bool HasCapturedOriginalValues => originalValues is not null;

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
        : this(model, table, type, snapshot: null)
    {
    }

    internal StateChange(
        IModelInstance model,
        TableDefinition table,
        TransactionChangeType type,
        MutationSnapshot? snapshot)
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

        PrimaryKeys = KeyFactory.GetKey(model, table.PrimaryKeyColumns);
        this.snapshot = snapshot ?? MutationSnapshot.Capture(
            model,
            table,
            captureInsertValues: type == TransactionChangeType.Insert);
        if (!ReferenceEquals(this.snapshot.Table, table))
            throw new ArgumentException("The mutation snapshot must belong to the state-change table.", nameof(snapshot));

        affectedIndices = CaptureAffectedIndices(table, type, this.snapshot);
        hasReloadablePrimaryKey = type == TransactionChangeType.Insert &&
            HasReloadablePrimaryKey(PrimaryKeys, table);
        hasIntegralCanonicalPrimaryKeyShape = type == TransactionChangeType.Insert &&
            ProviderKeyComponents.HasOnlyIntegralCanonicalComponents(table);
        CaptureOriginalValues(model);
    }

    /// <summary>
    /// Enumerates the captured mutation assignments. Array values are returned as detached copies.
    /// </summary>
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges() =>
        snapshot.GetDetachedChanges();

    internal bool TryGetOriginalValue(ColumnDefinition column, out object? value)
    {
        var ordinal = column.Index;
        if (originalValues is not null &&
            ordinal >= 0 &&
            ordinal < Table.ColumnCount &&
            ReferenceEquals(Table.Columns[ordinal], column) &&
            MutationSnapshot.IsBitSet(
                originalValueOccupancy,
                overflowOriginalValueOccupancy,
                ordinal))
        {
            value = originalValues[ordinal];
            return true;
        }

        value = null;
        return false;
    }

    internal bool HasSameCanonicalPrimaryKeyIdentity() =>
        PrimaryKeys.Equals(KeyFactory.GetKey(Model, Table.PrimaryKeyColumns));

    internal bool HasSameCapturedMutation() => snapshot.MatchesCurrent(Model);

    internal bool HasSameFinalizedMutation() =>
        finalizedMutationVersion is long version
            ? snapshot.MatchesCurrent(Model, version)
            : finalizedLegacySnapshot?.MatchesCurrent(Model) == true;

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

    private static bool HasReloadablePrimaryKey(
        DataLinqKey primaryKeys,
        TableDefinition table)
    {
        if (table.PrimaryKeyColumns.Count == 0)
            return false;

        var hasCompleteCanonicalKey =
            primaryKeys.ValueCount == table.PrimaryKeyColumns.Count;
        for (var index = 0;
             hasCompleteCanonicalKey && index < primaryKeys.ValueCount;
             index++)
        {
            var component = primaryKeys.GetValueUnsafe(index);
            hasCompleteCanonicalKey = component is not null &&
                !ReferenceEquals(component, DBNull.Value);
        }

        if (hasCompleteCanonicalKey)
            return true;

        return table.PrimaryKeyColumns.Count == 1 &&
            table.AutoIncrementPrimaryKeyColumn is { } autoIncrementPrimaryKey &&
            GeneratedValueDecoder.CanDecodeAutoIncrementValue(autoIncrementPrimaryKey);
    }

    private static IReadOnlyList<ColumnIndex> CaptureAffectedIndices(
        TableDefinition table,
        TransactionChangeType type,
        MutationSnapshot snapshot)
    {
        if (type is TransactionChangeType.Insert or TransactionChangeType.Delete)
            return table.ColumnIndices;

        if (type != TransactionChangeType.Update ||
            snapshot.IsEmpty ||
            table.ColumnIndices.Count == 0)
        {
            return [];
        }

        var affectedCount = 0;
        for (var index = 0; index < table.ColumnIndices.Count; index++)
        {
            if (IsAffected(table.ColumnIndices[index], snapshot))
                affectedCount++;
        }

        if (affectedCount == 0)
            return [];

        var affected = new ColumnIndex[affectedCount];
        var affectedPosition = 0;
        for (var index = 0; index < table.ColumnIndices.Count; index++)
        {
            var columnIndex = table.ColumnIndices[index];
            if (IsAffected(columnIndex, snapshot))
                affected[affectedPosition++] = columnIndex;
        }

        return affected;

        static bool IsAffected(
            ColumnIndex index,
            MutationSnapshot capturedSnapshot)
        {
            for (var columnIndex = 0; columnIndex < index.Columns.Count; columnIndex++)
            {
                if (capturedSnapshot.Contains(index.Columns[columnIndex]))
                    return true;
            }

            return false;
        }
    }

    private void CaptureOriginalValues(IModelInstance model)
    {
        if (Type == TransactionChangeType.Insert ||
            affectedIndices.Count == 0 ||
            model.GetRowData() is not MutableRowData rowData)
        {
            return;
        }

        for (var indexPosition = 0; indexPosition < affectedIndices.Count; indexPosition++)
        {
            var index = affectedIndices[indexPosition];
            for (var columnPosition = 0; columnPosition < index.Columns.Count; columnPosition++)
            {
                var column = index.Columns[columnPosition];
                var ordinal = column.Index;
                if (MutationSnapshot.IsBitSet(
                        originalValueOccupancy,
                        overflowOriginalValueOccupancy,
                        ordinal) ||
                    !rowData.TryGetOriginalValue(column, out var value))
                {
                    continue;
                }

                originalValues ??= new object?[Table.ColumnCount];
                originalValues[ordinal] = MutationSnapshot.SnapshotValue(value);
                MutationSnapshot.SetBit(
                    ref originalValueOccupancy,
                    ref overflowOriginalValueOccupancy,
                    ordinal,
                    Table.ColumnCount);
            }
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
            var command = PrepareExecutionCommand(transaction);
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

    private void FinalizeRelationKeysAfterExecution() =>
        finalizedRelationKeys = CaptureRelationKeys(Model);

    private Dictionary<ColumnIndex, DataLinqKey>? CaptureRelationKeys(
        IModelInstance model)
    {
        if (affectedIndices.Count == 0)
            return null;

        var relationKeys = new Dictionary<ColumnIndex, DataLinqKey>(affectedIndices.Count);
        for (var indexPosition = 0; indexPosition < affectedIndices.Count; indexPosition++)
        {
            var index = affectedIndices[indexPosition];
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
            if (!TryGetOriginalValue(index.Columns[columnIndex], out var value))
            {
                key = DataLinqKey.Null;
                return false;
            }

            values[columnIndex] = value;
        }

        key = KeyFactory.CreateKeyFromModelValues(values, index.Columns);
        return true;
    }

    internal void CaptureFinalizedMutation()
    {
        finalizedMutationVersion = Model is IMutableChangeTracking trackedMutable
            ? trackedMutable.MutationVersion
            : null;
        finalizedLegacySnapshot = finalizedMutationVersion is null
            ? MutationSnapshot.Capture(
                Model,
                Table,
                captureInsertValues: false)
            : null;
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

        return PrepareExecutionCommand(transaction);
    }

    internal IDbCommand PrepareExecutionCommand(Transaction transaction) =>
        transaction.Provider.ToDbCommand(PrepareExecutionQuery(transaction));

    /// <summary>
    /// Generates the query for the state change.
    /// </summary>
    /// <param name="transaction">The transaction the query is for.</param>
    /// <returns>The query representing the state change.</returns>
    public IQuery GetQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.EnsureMutationPreflight(this);

        return PrepareExecutionQuery(transaction);
    }

    internal IQuery PrepareExecutionQuery(Transaction transaction)
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

        for (var ordinal = 0; ordinal < Table.ColumnCount; ordinal++)
        {
            var column = Table.Columns[ordinal];
            var isAssigned = snapshot.Contains(column);
            var modelValue = snapshot.GetInsertModelValue(column);
            if (ShouldOmitUnsetAutoIncrementPrimaryKey(
                    column,
                    isAssigned,
                    modelValue,
                    supportsDefaultOnlyInsert) ||
                ShouldOmitUnsetServerDefault(
                    column,
                    isAssigned,
                    modelValue,
                    query.DataSource.Provider.DatabaseType))
            {
                continue;
            }

            var value = writer.ConvertModelColumnValue(
                column,
                modelValue,
                "mutation.insert");
            query.Set(column.DbName, value);
        }

        if (HasAutoIncrement)
            query.AddLastIdQuery();

        return query.InsertQuery();
    }

    private bool ShouldOmitUnsetAutoIncrementPrimaryKey(
        ColumnDefinition column,
        bool isAssigned,
        object? modelValue,
        bool supportsDefaultOnlyInsert) =>
        supportsDefaultOnlyInsert &&
        !isAssigned &&
        modelValue is null &&
        hasReloadablePrimaryKey &&
        ReferenceEquals(column, Table.AutoIncrementPrimaryKeyColumn);

    private bool ShouldOmitUnsetServerDefault(
        ColumnDefinition column,
        bool isAssigned,
        object? modelValue,
        DatabaseType databaseType)
    {
        if (isAssigned ||
            modelValue is not null ||
            !hasReloadablePrimaryKey ||
            column.PrimaryKey ||
            (column.HasScalarConverter &&
             !hasIntegralCanonicalPrimaryKeyShape) ||
            column.ColumnIndices.Any())
        {
            return false;
        }

        return column.ValueProperty.GetDefaultAttribute() is DefaultSqlAttribute defaultSql &&
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

        foreach (var change in snapshot)
            query.Set(
                change.Column.DbName,
                writer.ConvertModelColumnValue(
                    change.Column,
                    change.Value,
                    "mutation.update.value"));

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
