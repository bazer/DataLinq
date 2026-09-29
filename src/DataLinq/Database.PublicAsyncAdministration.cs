using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;

namespace DataLinq;

public abstract partial class Database<T> where T : class, IDatabaseModel<T>
{
    /// <summary>Checks file or server availability asynchronously without creating a database.</summary>
    public Task<bool> FileOrServerExistsAsync(CancellationToken cancellationToken = default) =>
        Provider.FileOrServerExistsAsync(cancellationToken);

    /// <summary>Checks database availability asynchronously using the provider's effective identity.</summary>
    public Task<bool> DatabaseExistsAsync(string? databaseName = null, CancellationToken cancellationToken = default) =>
        Provider.DatabaseExistsAsync(databaseName, cancellationToken);

    /// <summary>Checks table existence asynchronously. Cancellation and metadata query errors are not absence.</summary>
    public Task<bool> TableExistsAsync(string tableName, string? databaseName = null, CancellationToken cancellationToken = default) =>
        Provider.TableExistsAsync(tableName, databaseName, cancellationToken);
}
