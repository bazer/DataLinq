using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;

namespace DataLinq.Mutation;

/// <summary>A reusable factory bound to an existing database. It owns each callback transaction, but does not own the database.</summary>
public sealed class DataLinqUnitOfWorkFactory<TDatabase> : IDataLinqUnitOfWorkFactory<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly Database<TDatabase> database;

    /// <summary>Binds this factory to a database without opening a transaction or transferring database ownership.</summary>
    public DataLinqUnitOfWorkFactory(Database<TDatabase> database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    /// <inheritdoc />
    public IDataLinqUnitOfWork<TDatabase> Create(TransactionType transactionType = TransactionType.ReadAndWrite) => database.Transaction(transactionType);

    /// <inheritdoc />
    public void Execute(Action<IDataLinqSession<TDatabase>> action, TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        ArgumentNullException.ThrowIfNull(action);
        Execute(session => { action(session); return true; }, transactionType);
    }

    /// <inheritdoc />
    public TResult Execute<TResult>(Func<IDataLinqSession<TDatabase>, TResult> action, TransactionType transactionType = TransactionType.ReadAndWrite)
    {
        ArgumentNullException.ThrowIfNull(action);
        var transaction = database.Transaction(transactionType);
        return transaction.RunOwnedCallbackCore(() => action(transaction));
    }

    /// <inheritdoc />
    public Task ExecuteAsync(Func<IDataLinqSession<TDatabase>, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return database.CommitAsync(transaction => action(transaction), transactionType, cancellationToken);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(Func<IDataLinqSession<TDatabase>, CancellationToken, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return database.CommitAsync((transaction, token) => action(transaction, token), transactionType, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteAsync<TResult>(Func<IDataLinqSession<TDatabase>, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return database.CommitAsync(transaction => action(transaction), transactionType, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteAsync<TResult>(Func<IDataLinqSession<TDatabase>, CancellationToken, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return database.CommitAsync((transaction, token) => action(transaction, token), transactionType, cancellationToken);
    }
}
