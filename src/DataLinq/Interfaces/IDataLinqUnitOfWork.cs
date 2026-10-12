using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Interfaces;

/// <summary>Owns a transaction and its completion/cleanup, and can be passed directly to participating operations.</summary>
public interface IDataLinqUnitOfWork<TDatabase> : IDataLinqSession<TDatabase>, IDisposable, IAsyncDisposable
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    /// <summary>Commits when the transaction remains usable and no rollback was requested.</summary>
    void Commit();
    /// <summary>Asynchronously commits when the transaction remains usable and no rollback was requested.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Rolls back this transaction.</summary>
    void Rollback();
    /// <summary>Asynchronously rolls back this transaction.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
