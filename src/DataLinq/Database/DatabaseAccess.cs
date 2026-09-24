using System;
using System.Collections.Generic;
using System.Data;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class DatabaseAccess : IDatabaseAccess
{
    protected DatabaseAccess()
        : this(null)
    {
    }

    protected DatabaseAccess(IDatabaseProvider? databaseProvider)
    {
        TelemetryContext = DataLinqTelemetryContext.FromProvider(databaseProvider);
    }

    private protected DataLinqTelemetryContext TelemetryContext { get; }
    internal string? DiagnosticProviderInstanceId =>
        string.IsNullOrEmpty(TelemetryContext.ProviderInstanceId) ? null : TelemetryContext.ProviderInstanceId;

    public abstract IDataLinqDataReader ExecuteReader(IDbCommand command);
    public abstract IDataLinqDataReader ExecuteReader(string query);
    public abstract object? ExecuteScalar(IDbCommand command);
    public abstract T ExecuteScalar<T>(IDbCommand command);
    public abstract object? ExecuteScalar(string query);
    public abstract T ExecuteScalar<T>(string query);
    public abstract int ExecuteNonQuery(IDbCommand command);
    public abstract int ExecuteNonQuery(string query);

    /// <summary>
    /// Executes a newly created command and transfers its ownership to the returned
    /// reader. The IDbCommand overload itself leaves ownership with its caller.
    /// </summary>
    protected IDataLinqDataReader ExecuteOwnedReader(IDbCommand command)
    {
        try
        {
            return OwnedCommandDataReader.Create(ExecuteReader(command), command);
        }
        catch (Exception executionFailure)
        {
            try
            {
                command.Dispose();
            }
            catch (Exception disposalFailure)
            {
                var aggregate = new AggregateException("Reader creation and owned command disposal both failed.", executionFailure, disposalFailure);
                var failures = new ExecutionFailures();
                failures.AddReported(executionFailure, ExecutionFailureStage.CommandExecution);
                failures.AddCleanup(disposalFailure);
                ExecutionFailureContexts.Attach(aggregate, failures.Snapshot(new(),
                    managedTransaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                    managedTransaction is null ? ExecutionRecoveryActions.None : ExecutionRecoveryActions.Dispose,
                    managedTransaction?.TransactionID));
                throw aggregate;
            }
            throw;
        }
    }

    protected TResult ExecuteCommandWithTelemetry<TResult>(
        IDbCommand command,
        string commandKind,
        bool transactional,
        TransactionType? transactionType,
        Func<TResult> execute)
        => ExecuteCommandTelemetry(command, commandKind, transactional, transactionType, execute);

    public IEnumerable<IDataLinqDataReader> ReadReader(IDbCommand command)
    {
        using var reader = ExecuteReader(command);

        while (reader.ReadNextRow())
            yield return reader;
    }

    public IEnumerable<IDataLinqDataReader> ReadReader(string query)
    {
        using var reader = ExecuteReader(query);

        while (reader.ReadNextRow())
            yield return reader;
    }
}
