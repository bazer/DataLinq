using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public abstract class DatabaseAccess : IDatabaseAccess
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

    public abstract IDataLinqDataReader ExecuteReader(IDbCommand command);
    public abstract IDataLinqDataReader ExecuteReader(string query);
    public abstract object? ExecuteScalar(IDbCommand command);
    public abstract T ExecuteScalar<T>(IDbCommand command);
    public abstract object? ExecuteScalar(string query);
    public abstract T ExecuteScalar<T>(string query);
    public abstract int ExecuteNonQuery(IDbCommand command);
    public abstract int ExecuteNonQuery(string query);

    protected TResult ExecuteCommandWithTelemetry<TResult>(
        IDbCommand command,
        string commandKind,
        bool transactional,
        TransactionType? transactionType,
        Func<TResult> execute)
    {
        var operation = DataLinqTelemetry.GetCommandOperation(command);
        using var activity = DataLinqTelemetry.StartCommandActivity(
            TelemetryContext,
            commandKind,
            operation,
            transactional,
            transactionType);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = execute();
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordCommand(TelemetryContext, commandKind, operation, transactional, transactionType, succeeded: true, duration);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordCommand(TelemetryContext, commandKind, operation, transactional, transactionType, succeeded: false, duration);
            DataLinqTelemetry.RecordException(activity, ex);
            throw;
        }
    }

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
