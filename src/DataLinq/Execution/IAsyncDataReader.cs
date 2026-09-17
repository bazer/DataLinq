using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Internal asynchronous cursor capability. Current-row getters remain synchronous and must
/// not perform deferred provider I/O; advancement makes the needed values available first.
/// </summary>
/// <remarks>
/// The current row is borrowed until the next advance or disposal, not an independent
/// snapshot. The owner must await outstanding work before disposing the reader. Async
/// disposal uses the implemented async path, never a fallback to synchronous disposal.
/// A transaction-bound reader does not own its surrounding transaction or connection.
/// </remarks>
internal interface IAsyncDataReader : IDataLinqDataReader, IAsyncDisposable
{
    Task<bool> ReadNextRowAsync(CancellationToken cancellationToken);
}
