using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Internal first-use resource bundle. Construction does no I/O. Initialization owns all
/// partial resources until opening, configuration and begin have succeeded. Implementations
/// provide direct sync and explicit async paths, including cleanup of partial initialization.
/// This is not yet implemented by the production providers.
/// </summary>
internal interface ITransactionResource : IDisposable, IAsyncDisposable
{
    void Initialize();
    Task InitializeAsync(CancellationToken cancellationToken);
}
