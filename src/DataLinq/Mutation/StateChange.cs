using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Mutation;

/// <summary>
/// Represents a change of state to be applied to a model within a transaction.
/// </summary>
public class StateChange
{
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
    public TableMetadata Table { get; }

    /// <summary>
    /// Gets the primary keys for the model.
    /// </summary>
    public IKey PrimaryKeys { get; }

    /// <summary>
    /// Determines if the model has an auto-incrementing primary key.
    /// </summary>
    public bool HasAutoIncrement =>
        Table.PrimaryKeyColumns.Any(x => x.AutoIncrement);

    /// <summary>
    /// Initializes a new instance of the <see cref="StateChange"/> class.
    /// </summary>
    /// <param name="model">The model to apply the change to.</param>
    /// <param name="table">The table metadata for the model.</param>
    /// <param name="type">The type of change to be applied.</param>
    public StateChange(IModelInstance model, TableMetadata table, TransactionChangeType type)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(table);

        if (table.Type == TableType.View)
            throw new InvalidOperationException("Cannot change a view.");

        if (type == TransactionChangeType.Update && model is not IMutableInstance)
            throw new InvalidOperationException("Cannot update a model that is not mutable.");

        Model = model;
        Table = table;
        Type = type;

        PrimaryKeys = model.PrimaryKeys();
    }

    public IEnumerable<KeyValuePair<Column, object>> GetValues() =>
        GetValues(Table.Columns);

    public IEnumerable<KeyValuePair<Column, object>> GetValues(IEnumerable<Column> columns)
    {
        if (Model is IModelInstance instance)
            return instance.GetValues(columns);
        else
            return Model.GetValues(columns);
    }

    public IEnumerable<KeyValuePair<Column, object>> GetChanges()
    {
        if (Model is IMutableInstance instance)
            return instance.GetChanges();
        else
            return GetValues();
    }

    /// <summary>
    /// Executes the query associated with the state change on the given transaction.
    /// </summary>
    /// <param name="transaction">The transaction to execute the query on.</param>
    public void ExecuteQuery(Transaction transaction)
    {
        if (Type == TransactionChangeType.Insert && HasAutoIncrement && !Model.HasPrimaryKeysSet())
        {
            var newId = transaction.DatabaseAccess.ExecuteScalar(GetDbCommand(transaction));

            if (Model is IMutableInstance mutable)
            {
                var autoIncrement = Table.PrimaryKeyColumns.FirstOrDefault(x => x.AutoIncrement);

                if (autoIncrement != null)
                    mutable[autoIncrement] = newId;
            }
        }
        else
            transaction.DatabaseAccess.ExecuteNonQuery(GetDbCommand(transaction));
    }

    /// <summary>
    /// Creates a database command for the state change to be executed within the transaction.
    /// </summary>
    /// <param name="transaction">The transaction the command is for.</param>
    /// <returns>The database command to execute.</returns>
    public IDbCommand GetDbCommand(Transaction transaction) =>
        transaction.Provider.ToDbCommand(GetQuery(transaction));

    /// <summary>
    /// Generates the query for the state change.
    /// </summary>
    /// <param name="transaction">The transaction the query is for.</param>
    /// <returns>The query representing the state change.</returns>
    public IQuery GetQuery(Transaction transaction)
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
        foreach (var column in Table.Columns)
        {
            var val = writer.ConvertColumnValue(column, Model[column]);
            query.Set(column.DbName, val);
        }

        if (HasAutoIncrement)
            query.AddLastIdQuery();

        return query.InsertQuery();
    }

    private IQuery BuildUpdateQuery(SqlQuery query, IDataLinqDataWriter writer)
    {
        foreach (var column in Table.PrimaryKeyColumns)
            query.Where(column.DbName).EqualTo(writer.ConvertColumnValue(column, Model[column]));

        foreach (var change in ((IMutableInstance)Model).GetChanges())
            query.Set(change.Key.DbName, writer.ConvertColumnValue(change.Key, change.Value));

        return query.UpdateQuery();
    }

    private IQuery BuildDeleteQuery(SqlQuery query, IDataLinqDataWriter writer)
    {
        foreach (var column in Table.PrimaryKeyColumns)
            query.Where(column.DbName).EqualTo(writer.ConvertColumnValue(column, Model[column]));

        return query.DeleteQuery();
    }
}