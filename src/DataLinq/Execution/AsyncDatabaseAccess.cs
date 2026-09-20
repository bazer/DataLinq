using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

internal enum AsyncCommandKind
{
    Reader,
    Scalar,
    NonQuery
}

/// <summary>
/// Applies the common pre-dispatch ordering without changing existing synchronous access.
/// Provider adapters supply validation and actual async execution in a later wave.
/// </summary>
internal abstract class AsyncDatabaseAccess : IAsyncDatabaseAccess
{
    internal static IAsyncDatabaseAccess Require(object access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return access as IAsyncDatabaseAccess
            ?? throw new NotSupportedException("This database access does not implement asynchronous execution.");
    }

    public Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        Validate(command, AsyncCommandKind.Reader, cancellationToken);
        return ExecuteReaderCoreAsync(command, cancellationToken);
    }

    public void ValidateReader(IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command, AsyncCommandKind.Reader);
    }

    void IAsyncDatabaseAccess.ValidateCommand(IDbCommand command, AsyncCommandKind kind)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command, kind);
    }

    public Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        Validate(command, AsyncCommandKind.Scalar, cancellationToken);
        return ExecuteScalarCoreAsync(command, cancellationToken);
    }

    public Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        Validate(command, AsyncCommandKind.NonQuery, cancellationToken);
        return ExecuteNonQueryCoreAsync(command, cancellationToken);
    }

    private void Validate(IDbCommand command, AsyncCommandKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var diagnostics = ExecutionFailureScope.Begin();
        try
        {
            ValidateCommand(command, kind);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception failure)
        {
            // The enclosing source may already have entered adapter dispatch. These
            // I/O-free checks prove that this command's provider core never ran.
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Validation,
                failure is OperationCanceledException canceled && canceled.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested
                    ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
            CommandDispatchEvidence.Attach(failure, failures.Snapshot(new(Effects: ExecutionEffects.NoStatement),
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null), command, dispatched: false);
            throw;
        }
    }

    /// <summary>
    /// Performs ordinary argument, lifecycle and capability checks without I/O, mutation of
    /// the borrowed command, or transaction admission. These checks precede pre-cancellation.
    /// Operation admission and its lifetime belong to the surrounding execution orchestration.
    /// </summary>
    protected abstract void ValidateCommand(IDbCommand command, AsyncCommandKind kind);

    protected abstract Task<IAsyncDataReader> ExecuteReaderCoreAsync(IDbCommand command, CancellationToken cancellationToken);
    protected abstract Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken cancellationToken);
    protected abstract Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken cancellationToken);
}
