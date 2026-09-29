using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Interfaces;

public partial interface IDatabaseProvider
{
    /// <summary>Checks file or server availability. Legacy providers without an async capability reject this call.</summary>
    Task<bool> FileOrServerExistsAsync(CancellationToken cancellationToken = default) =>
        AsyncExistenceProbes.FileOrServerExistsAsyncCore(this, cancellationToken);

    /// <summary>Checks database availability without implicitly creating it.</summary>
    Task<bool> DatabaseExistsAsync(string? databaseName = null, CancellationToken cancellationToken = default) =>
        AsyncExistenceProbes.DatabaseExistsAsyncCore(this, databaseName, cancellationToken);

    /// <summary>Checks table existence. Cancellation, unsupported capability and query failures remain exceptions.</summary>
    Task<bool> TableExistsAsync(string tableName, string? databaseName = null, CancellationToken cancellationToken = default) =>
        AsyncExistenceProbes.TableExistsAsyncCore(this, tableName, databaseName, cancellationToken);
}
