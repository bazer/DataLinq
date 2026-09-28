using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq;

/// <summary>An asynchronous cursor whose getters inspect the already available current row.</summary>
/// <remarks>
/// Current rows are borrowed until advancement or disposal. Await outstanding work before disposal.
/// Direct readers own their cleanup, but never complete their surrounding transaction. Row views
/// returned by sequence helpers cannot be advanced or disposed by the consumer.
/// </remarks>
public interface IDataLinqAsyncDataReader : IDataLinqDataReader, IAsyncDisposable
{
    /// <summary>Advances to the next row without falling back to synchronous reader execution.</summary>
    Task<bool> ReadNextRowAsync(CancellationToken cancellationToken = default);
}
