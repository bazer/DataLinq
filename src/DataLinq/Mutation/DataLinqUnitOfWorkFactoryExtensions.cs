using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Interfaces;

namespace DataLinq.Mutation;

/// <summary>Explicitly selected conveniences for optional native result payloads.</summary>
/// <remarks>
/// Result contents never request rollback. A valid rollback payload may return after commit
/// when no veto exists, or after a veto only when the owning helper confirms rollback/no
/// started provider work and successful cleanup. Operational failures remain exceptions.
/// </remarks>
public static class DataLinqUnitOfWorkFactoryExtensions
{
    /// <summary>Validates a native result before completion and permits a confirmed, successfully cleaned-up rollback outcome.</summary>
    public static TransactionResult<T, TFailure> ExecuteResult<TDatabase, T, TFailure>(
        this IDataLinqUnitOfWorkFactory<TDatabase> factory, Func<IDataLinqSession<TDatabase>, TransactionResult<T, TFailure>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite)
        where TDatabase : class, IDatabaseModel<TDatabase> where TFailure : notnull =>
        ExecuteCore(factory, action, result => result.IsSuccess, transactionType);

    /// <summary>Validates a string-reason result before completion and permits a confirmed, successfully cleaned-up rollback outcome.</summary>
    public static TransactionResult<T> ExecuteResult<TDatabase, T>(
        this IDataLinqUnitOfWorkFactory<TDatabase> factory, Func<IDataLinqSession<TDatabase>, TransactionResult<T>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite)
        where TDatabase : class, IDatabaseModel<TDatabase> =>
        ExecuteCore(factory, action, result => result.IsSuccess, transactionType);

    /// <summary>Awaits a native-result callback, validation, owned completion and cleanup.</summary>
    public static Task<TransactionResult<T, TFailure>> ExecuteResultAsync<TDatabase, T, TFailure>(
        this IDataLinqUnitOfWorkFactory<TDatabase> factory, Func<IDataLinqSession<TDatabase>, CancellationToken, Task<TransactionResult<T, TFailure>>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        where TDatabase : class, IDatabaseModel<TDatabase> where TFailure : notnull =>
        ExecuteCoreAsync(factory, action, result => result.IsSuccess, transactionType, cancellationToken);

    /// <summary>Awaits a string-reason callback, validation, owned completion and cleanup.</summary>
    public static Task<TransactionResult<T>> ExecuteResultAsync<TDatabase, T>(
        this IDataLinqUnitOfWorkFactory<TDatabase> factory, Func<IDataLinqSession<TDatabase>, CancellationToken, Task<TransactionResult<T>>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        where TDatabase : class, IDatabaseModel<TDatabase> =>
        ExecuteCoreAsync(factory, action, result => result.IsSuccess, transactionType, cancellationToken);

    /// <summary>Awaits a native-result callback, validation, owned completion and cleanup.</summary>
    public static Task<TransactionResult<T, TFailure>> ExecuteResultAsync<TDatabase, T, TFailure>(
        this IDataLinqUnitOfWorkFactory<TDatabase> factory, Func<IDataLinqSession<TDatabase>, Task<TransactionResult<T, TFailure>>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        where TDatabase : class, IDatabaseModel<TDatabase> where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteCoreAsync(factory, (session, _) => action(session), result => result.IsSuccess, transactionType, cancellationToken);
    }

    /// <summary>Awaits a string-reason callback, validation, owned completion and cleanup.</summary>
    public static Task<TransactionResult<T>> ExecuteResultAsync<TDatabase, T>(
        this IDataLinqUnitOfWorkFactory<TDatabase> factory, Func<IDataLinqSession<TDatabase>, Task<TransactionResult<T>>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteCoreAsync(factory, (session, _) => action(session), result => result.IsSuccess, transactionType, cancellationToken);
    }

    private static TResult ExecuteCore<TDatabase, TResult>(IDataLinqUnitOfWorkFactory<TDatabase> factory,
        Func<IDataLinqSession<TDatabase>, TResult> action, Func<TResult, bool> isSuccess, TransactionType transactionType)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        var state = new ResultCallback<TDatabase, TResult>(isSuccess);
        try { return factory.Execute(session => state.Validate(session, action(session)), transactionType); }
        catch (DataLinqRollbackRequestedException failure) when (state.CanReturn(failure)) { return state.Result; }
    }

    private static async Task<TResult> ExecuteCoreAsync<TDatabase, TResult>(IDataLinqUnitOfWorkFactory<TDatabase> factory,
        Func<IDataLinqSession<TDatabase>, CancellationToken, Task<TResult>> action, Func<TResult, bool> isSuccess,
        TransactionType transactionType, CancellationToken cancellationToken)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        var state = new ResultCallback<TDatabase, TResult>(isSuccess);
        try
        {
            return await factory.ExecuteAsync(async (session, token) => state.Validate(session,
                await (action(session, token) ?? throw new InvalidOperationException("The transaction callback returned a null task.")).ConfigureAwait(false)),
                transactionType, cancellationToken).ConfigureAwait(false);
        }
        catch (DataLinqRollbackRequestedException failure) when (state.CanReturn(failure)) { return state.Result; }
    }

    private sealed class ResultCallback<TDatabase, TResult>(Func<TResult, bool> isSuccess)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        private Transaction? owner;
        private bool rollbackPayload;
        internal TResult Result { get; private set; } = default!;

        internal TResult Validate(IDataLinqSession<TDatabase> session, TResult result)
        {
            // Inspect inside the callback, before commit admission. Invalid/default
            // values throw normally without changing the participant's rollback state.
            rollbackPayload = !isSuccess(result);
            owner = session as Transaction;
            return Result = result;
        }

        internal bool CanReturn(DataLinqRollbackRequestedException failure) => rollbackPayload &&
            owner is not null && failure.ConfirmedHelperTransactionId == owner.TransactionID;
    }
}
