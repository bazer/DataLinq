using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    private Transaction? managedTransaction;

    internal void BindManagedTransaction(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (Interlocked.CompareExchange(ref managedTransaction, transaction, null) is not null)
            throw new InvalidOperationException("This database access already belongs to a managed transaction.");
    }

    internal Task<int> ExecuteNonQueryAsyncCore(string sql, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(Bind(sql), null, cancellationToken);

    internal Task<int> ExecuteNonQueryAsyncCore(IDbCommand command, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(Bind(command), null, cancellationToken);

    internal Task<object?> ExecuteScalarAsyncCore(string sql, CancellationToken cancellationToken = default)
        => ExecuteScalarAsync(Bind(sql), static value => value, null, cancellationToken);

    internal Task<object?> ExecuteScalarAsyncCore(IDbCommand command, CancellationToken cancellationToken = default)
        => ExecuteScalarAsync(Bind(command), static value => value, null, cancellationToken);

    internal Task<T> ExecuteScalarAsyncCore<T>(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var invocation = IAsyncEagerCommandFactory.Require(this).BindScalar<T>(sql);
        return ExecuteScalarAsync(invocation.Command, invocation.Convert, null, cancellationToken);
    }

    internal Task<T> ExecuteScalarAsyncCore<T>(IDbCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invocation = IAsyncEagerCommandFactory.Require(this).BindScalar<T>(command);
        return ExecuteScalarAsync(invocation.Command, invocation.Convert, null, cancellationToken);
    }

    // Only internal managed execution can supply this proof of existing admission. It is
    // never exposed to user conversion/callbacks and is validated against this transaction.
    internal Task<object?> ExecuteScalarOwnedAsyncCore(IDbCommand command, TransactionOperationGate.Step owner, CancellationToken token)
        => ExecuteScalarAsync(Bind(command), static value => value, owner, token);

    internal Task<int> ExecuteNonQueryOwnedAsyncCore(IDbCommand command, TransactionOperationGate.Step owner, CancellationToken token)
        => ExecuteNonQueryAsync(Bind(command), owner, token);

    private AsyncEagerCommand Bind(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        return IAsyncEagerCommandFactory.Require(this).BindCommand(sql);
    }

    private AsyncEagerCommand Bind(IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return IAsyncEagerCommandFactory.Require(this).BindCommand(command);
    }

    private Task<int> ExecuteNonQueryAsync(AsyncEagerCommand command, TransactionOperationGate.Step? owner, CancellationToken token)
        => ExecuteEagerAsync(command, AsyncCommandKind.NonQuery, owner, token,
            static (command, owner, token) => command.ExecuteNonQueryAsync(owner, token), static value => value);

    private Task<T> ExecuteScalarAsync<T>(AsyncEagerCommand command, Func<object?, T> convert,
        TransactionOperationGate.Step? owner, CancellationToken token)
        => ExecuteEagerAsync(command, AsyncCommandKind.Scalar, owner, token,
            static (command, owner, token) => command.ExecuteScalarAsync(owner, token), convert);

    private async Task<TResult> ExecuteEagerAsync<TValue, TResult>(AsyncEagerCommand command, AsyncCommandKind kind,
        TransactionOperationGate.Step? owner, CancellationToken token,
        Func<AsyncEagerCommand, TransactionOperationGate.Step?, CancellationToken, Task<TValue>> execute,
        Func<TValue, TResult> convert)
    {
        const string operation = "execute an asynchronous command";
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(convert);
        var transaction = managedTransaction;
        if (transaction is null && (owner is not null || this is DatabaseTransaction))
            throw new InvalidOperationException("Transaction command execution requires its managed transaction owner.");
        var operationKind = owner?.Kind ?? ExecutionOperationKind.RawCommand;
        transaction?.EnsureCanRead(operation, owner, operationKind);
        command.Validate(kind, transaction is not null);
        token.ThrowIfCancellationRequested();
        using var ownership = transaction is not null && owner is null
            ? DataSourceAccess.BeginRead(transaction, operation, cancellationToken: token, operationKind: operationKind) : null;
        var step = owner ?? ownership?.Step;
        var stage = ExecutionFailureStage.CommandExecution;
        var cause = ExecutionFailureCause.Unknown;
        try
        {
            var result = await execute(command, step, token).ConfigureAwait(false);
            stage = ExecutionFailureStage.Materialization;
            cause = ExecutionFailureCause.MaterializationError;
            // Owned cleanup has settled. A late cancellation request cannot undo success.
            return convert(result);
        }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.AddReported(failure, stage, failure is OperationCanceledException canceled &&
                canceled.CancellationToken == token && token.IsCancellationRequested ? ExecutionFailureCause.Cancellation : cause);
            var evidence = new ReadFailureEvidence();
            var assessed = true;
            try
            {
                evidence = command.GetReadFailureEvidence(failure)
                    ?? throw new InvalidOperationException("The provider returned no failure evidence.");
                // Raw scalar/non-query results say nothing about side effects. Even a
                // provider's OrdinaryRead/NoStatement claim cannot restore raw reuse.
                if (owner is null && command.Dispatched)
                    evidence = evidence with { Effects = ExecutionEffects.Unknown };
            }
            catch (Exception assessment)
            {
                assessed = false;
                failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
            }
            var recovery = transaction is null ? ExecutionRecoveryActions.None
                : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessed);
            var context = failures.Snapshot(evidence, transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                recovery, transaction?.TransactionID, operationKind, transaction?.ExecutionGate.ProviderInstanceId);
            if (step is not null) transaction!.RecordAsyncReadFailure(step, context);
            ExecutionFailureContexts.Attach(failure, context);
            ownership?.ReportFailure(failure);
            throw;
        }
    }
}
