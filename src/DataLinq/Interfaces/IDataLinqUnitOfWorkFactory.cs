using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Interfaces;

/// <summary>Creates independent units of work bound to one database, or owns execution of a participating callback.</summary>
public interface IDataLinqUnitOfWorkFactory<TDatabase> where TDatabase : class, IDatabaseModel<TDatabase>
{
    /// <summary>Creates a logical transaction without eagerly opening a provider connection. The caller owns completion and disposal.</summary>
    IDataLinqUnitOfWork<TDatabase> Create(TransactionType transactionType = TransactionType.ReadAndWrite);
    /// <summary>Runs a callback once and returns only after successful commit and cleanup. An explicit veto rolls back and throws.</summary>
    void Execute(Action<IDataLinqSession<TDatabase>> action, TransactionType transactionType = TransactionType.ReadAndWrite);
    /// <summary>Runs a callback once and returns its ordinary/custom/native payload only after successful commit and cleanup.</summary>
    TResult Execute<TResult>(Func<IDataLinqSession<TDatabase>, TResult> action, TransactionType transactionType = TransactionType.ReadAndWrite);
    /// <summary>Runs an async callback once, awaiting all work before completion and independent recovery/cleanup.</summary>
    Task ExecuteAsync(Func<IDataLinqSession<TDatabase>, Task> action, TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default);
    /// <summary>Runs an async callback once with an explicit token, awaiting completion and cleanup.</summary>
    Task ExecuteAsync(Func<IDataLinqSession<TDatabase>, CancellationToken, Task> action, TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default);
    /// <summary>Runs an async callback once and returns its payload only after successful commit and cleanup.</summary>
    Task<TResult> ExecuteAsync<TResult>(Func<IDataLinqSession<TDatabase>, Task<TResult>> action, TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default);
    /// <summary>Runs an async callback once with an explicit token and returns its payload only after successful commit and cleanup.</summary>
    Task<TResult> ExecuteAsync<TResult>(Func<IDataLinqSession<TDatabase>, CancellationToken, Task<TResult>> action, TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default);
}
