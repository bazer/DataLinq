using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
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

/// <summary>
/// Represents a change of state to be applied to a model within a transaction.
/// </summary>
public class StateChange
{
    private readonly IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> changes;
    private readonly IReadOnlyList<MutationWriteSlot> insertWriteSlots;
    private readonly Dictionary<ColumnDefinition, object?> originalValues = new();
    private readonly bool hasReloadableIdentityMappedPrimaryKey;

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
    /// Gets the primary keys for the model.
    /// </summary>
    public DataLinqKey PrimaryKeys { get; }

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

        PrimaryKeys = KeyFactory.GetKey(model, table.PrimaryKeyColumns);
        changes = CaptureChanges(model);
        insertWriteSlots = type == TransactionChangeType.Insert
            ? CaptureInsertWriteSlots(model, table, changes)
            : [];
        hasReloadableIdentityMappedPrimaryKey = type == TransactionChangeType.Insert &&
            HasReloadableIdentityMappedPrimaryKey(model, table);
        CaptureOriginalValues(model);
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges()
    {
        for (var index = 0; index < changes.Count; index++)
            yield return changes[index];
    }

    internal bool TryGetOriginalValue(ColumnDefinition column, out object? value) =>
        originalValues.TryGetValue(column, out value);

    internal IReadOnlyList<MutationWriteSlot> GetInsertWriteSlots() =>
        insertWriteSlots;

    internal bool HasSameCanonicalPrimaryKeyIdentity() =>
        PrimaryKeys.Equals(KeyFactory.GetKey(Model, Table.PrimaryKeyColumns));

    private static IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> CaptureChanges(IModelInstance model) =>
        model is IMutableInstance mutable
            ? mutable.GetChanges().ToArray()
            : [];

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
            table.AutoIncrementPrimaryKeyColumn is not null;
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
                isAssigned ? assignedValue : model[column]);
        }

        return slots;
    }

    private void CaptureOriginalValues(IModelInstance model)
    {
        if (model.GetRowData() is not MutableRowData rowData)
            return;

        foreach (var change in changes)
        {
            if (rowData.TryGetOriginalValue(change.Key, out var value))
                originalValues[change.Key] = value;
        }
    }

    /// <summary>
    /// Executes the query associated with the state change on the given transaction.
    /// </summary>
    /// <param name="transaction">The transaction to execute the query on.</param>
    public void ExecuteQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.EnsureMutationPreflight(this);

        ExecuteQueryCore(transaction);
    }

    internal void ExecutePreflightedQuery(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ExecuteQueryCore(transaction);
    }

    private void ExecuteQueryCore(Transaction transaction)
    {
        var telemetryContext = DataLinqTelemetryContext.FromProvider(transaction.Provider);
        var activity = DataLinqTelemetry.StartMutationActivity(telemetryContext, Table.DbName, Type, transaction.Type);
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;
        var affectedRows = 0;

        try
        {
            if (Type == TransactionChangeType.Insert && HasAutoIncrement && PrimaryKeys.IsNull)
            {
                var newId = transaction.DatabaseAccess.ExecuteScalar(GetDbCommandCore(transaction));
                affectedRows = 1;

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
                affectedRows = transaction.DatabaseAccess.ExecuteNonQuery(GetDbCommandCore(transaction));

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
        foreach (var slot in insertWriteSlots)
        {
            if (ShouldOmitUnsetServerDefault(slot, query.DataSource.Provider.DatabaseType))
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
