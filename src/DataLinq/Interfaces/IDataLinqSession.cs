using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Interfaces;

/// <summary>Participates in an existing unit of work without ownership, completion, disposal or raw provider access.</summary>
/// <remarks>Returned mutations are provisional until the owner commits. Do not execute concurrent operations on one session.</remarks>
public interface IDataLinqSession<TDatabase> where TDatabase : class, IDatabaseModel<TDatabase>
{
    /// <summary>Validates that a new participating operation may start, without provider I/O or completion.</summary>
    /// <remarks>Rejects ended, vetoed, poisoned, busy or closed sessions. Each subsequent database operation still performs its own admission checks.</remarks>
    void ValidateParticipation();
    /// <summary>Gets the transaction-bound model query root.</summary>
    TDatabase Query();
    /// <summary>Looks up a model by canonical provider key.</summary>
    M? Get<M>(DataLinqKey key) where M : IImmutableInstance;
    /// <summary>Asynchronously looks up a model by canonical provider key.</summary>
    ValueTask<M?> GetAsync<M>(DataLinqKey key, CancellationToken cancellationToken = default) where M : IImmutableInstance;
    /// <summary>Creates a transaction-bound SQL query.</summary>
    SqlQuery From(string tableName);
    /// <summary>Creates a transaction-bound SQL query.</summary>
    SqlQuery From(TableDefinition table);
    /// <summary>Creates a transaction-bound model query.</summary>
    SqlQuery<M> From<M>() where M : IModel;

    /// <summary>Executes a prepared scalar or row query on this session.</summary>
    TResult Execute<TArgument, TResult>(PreparedQuery<TDatabase, TArgument, TResult> prepared, TArgument argument, CancellationToken cancellationToken = default);
    /// <summary>Executes a prepared scalar or row query asynchronously on this session.</summary>
    Task<TResult> ExecuteAsync<TArgument, TResult>(PreparedQuery<TDatabase, TArgument, TResult> prepared, TArgument argument, CancellationToken cancellationToken = default);
    /// <summary>Captures invocation values for a deferred sequence on this session. Enumeration must finish within its owning lifetime.</summary>
    IEnumerable<TElement> Execute<TArgument, TElement>(PreparedSequenceQuery<TDatabase, TArgument, TElement> prepared, TArgument argument, CancellationToken cancellationToken = default);
    /// <summary>Captures invocation values for a deferred async sequence on this session. Dispose each enumerator within its owning lifetime.</summary>
    IAsyncEnumerable<TElement> ExecuteAsync<TArgument, TElement>(PreparedSequenceQuery<TDatabase, TArgument, TElement> prepared, TArgument argument, CancellationToken cancellationToken = default);

    /// <summary>Inserts a mutable and returns the authoritative immutable row.</summary>
    M Insert<M>(Mutable<M> model) where M : class, IImmutableInstance;
    /// <summary>Applies local changes and inserts the row.</summary>
    M Insert<M>(Mutable<M> model, Action<Mutable<M>> changes) where M : class, IImmutableInstance;
    /// <summary>Applies typed local changes and inserts the row.</summary>
    M Insert<M, TMutable>(TMutable model, Action<TMutable> changes) where M : class, IImmutableInstance where TMutable : Mutable<M>;
    /// <summary>Captures a finite collection and inserts its rows.</summary>
    List<M> Insert<M>(IEnumerable<Mutable<M>> models) where M : class, IImmutableInstance;
    /// <summary>Asynchronously inserts a mutable and returns the authoritative row.</summary>
    Task<M> InsertAsync<M>(Mutable<M> model, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Applies local changes before suspension and inserts the row.</summary>
    Task<M> InsertAsync<M>(Mutable<M> model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Applies typed local changes before suspension and inserts the row.</summary>
    Task<M> InsertAsync<M, TMutable>(TMutable model, Action<TMutable> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance where TMutable : Mutable<M>;
    /// <summary>Captures a finite collection once before suspension and inserts its rows.</summary>
    Task<List<M>> InsertAsync<M>(IEnumerable<Mutable<M>> models, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;

    /// <summary>Updates a mutable and returns the authoritative immutable row.</summary>
    M Update<M>(Mutable<M> model) where M : class, IImmutableInstance;
    /// <summary>Applies local changes and updates the row.</summary>
    M Update<M>(Mutable<M> model, Action<Mutable<M>> changes) where M : class, IImmutableInstance;
    /// <summary>Applies typed local changes and updates the row.</summary>
    M Update<M, TMutable>(TMutable model, Action<TMutable> changes) where M : class, IImmutableInstance where TMutable : Mutable<M>;
    /// <summary>Creates a mutable from an immutable row, applies changes and updates it.</summary>
    M Update<M>(M model, Action<Mutable<M>> changes) where M : class, IImmutableInstance;
    /// <summary>Asynchronously updates a mutable and returns the authoritative row.</summary>
    Task<M> UpdateAsync<M>(Mutable<M> model, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Applies local changes before suspension and updates the row.</summary>
    Task<M> UpdateAsync<M>(Mutable<M> model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Applies typed local changes before suspension and updates the row.</summary>
    Task<M> UpdateAsync<M, TMutable>(TMutable model, Action<TMutable> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance where TMutable : Mutable<M>;
    /// <summary>Creates a mutable, applies changes before suspension and updates the row.</summary>
    Task<M> UpdateAsync<M>(M model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;

    /// <summary>Inserts or updates a mutable according to its lifecycle.</summary>
    M Save<M>(Mutable<M> model) where M : class, IImmutableInstance;
    /// <summary>Applies local changes and saves the row. Null starts a new mutable.</summary>
    M Save<M>(Mutable<M>? model, Action<Mutable<M>> changes) where M : class, IImmutableInstance;
    /// <summary>Applies typed local changes and saves the row.</summary>
    M Save<M, TMutable>(TMutable model, Action<TMutable> changes) where M : class, IImmutableInstance where TMutable : Mutable<M>;
    /// <summary>Creates a mutable, applies changes and saves it. Null starts a new mutable.</summary>
    M Save<M>(M? model, Action<Mutable<M>> changes) where M : class, IImmutableInstance;
    /// <summary>Asynchronously inserts or updates a mutable according to its lifecycle.</summary>
    Task<M> SaveAsync<M>(Mutable<M> model, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Applies local changes before suspension and saves the row. Null starts a new mutable.</summary>
    Task<M> SaveAsync<M>(Mutable<M>? model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Applies typed local changes before suspension and saves the row.</summary>
    Task<M> SaveAsync<M, TMutable>(TMutable model, Action<TMutable> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance where TMutable : Mutable<M>;
    /// <summary>Creates a mutable and applies changes before suspension. Null starts a new mutable.</summary>
    Task<M> SaveAsync<M>(M? model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default) where M : class, IImmutableInstance;
    /// <summary>Deletes a model without completing the transaction.</summary>
    void Delete(IModelInstance model);
    /// <summary>Asynchronously deletes a model without completing the transaction.</summary>
    Task DeleteAsync(IModelInstance model, CancellationToken cancellationToken = default);

    /// <summary>Gets whether a business veto was accepted. False does not establish commit eligibility.</summary>
    bool IsRollbackRequested { get; }
    /// <summary>Irreversibly requests rollback without provider I/O or interrupting admitted work.</summary>
    Rollback<TFailure> RequestRollback<TFailure>(TFailure reason) where TFailure : notnull;
    /// <summary>Gets the first accepted rollback reason when assignable to the requested type.</summary>
    bool TryGetRollbackReason<TFailure>([MaybeNullWhen(false)] out TFailure reason) where TFailure : notnull;
    /// <summary>Gets the current operational failure snapshot, independently of any business veto.</summary>
    DataLinqFailureContext? FailureContext { get; }
}
