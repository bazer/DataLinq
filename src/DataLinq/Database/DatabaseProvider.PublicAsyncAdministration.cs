using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq;

public abstract partial class DatabaseProvider
{
    /// <summary>Checks file or server availability through a supported async provider capability.</summary>
    public virtual Task<bool> FileOrServerExistsAsync(CancellationToken cancellationToken = default) =>
        this.FileOrServerExistsAsyncCore(cancellationToken);

    /// <summary>Checks database availability without creating a missing database.</summary>
    public virtual Task<bool> DatabaseExistsAsync(string? databaseName = null, CancellationToken cancellationToken = default) =>
        this.DatabaseExistsAsyncCore(databaseName, cancellationToken);

    /// <summary>Checks table existence. Cancellation and metadata query errors are not false results.</summary>
    public virtual Task<bool> TableExistsAsync(string tableName, string? databaseName = null, CancellationToken cancellationToken = default) =>
        this.TableExistsAsyncCore(tableName, databaseName, cancellationToken);
}
