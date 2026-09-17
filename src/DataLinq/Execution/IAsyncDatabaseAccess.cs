using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Internal command-execution capability. A command is borrowed for the entire operation
/// (and returned reader lifetime); its caller retains ownership and must keep it stable.
/// </summary>
/// <remarks>
/// These are implementation contracts, not public provider support claims. Implementations
/// must validate the selected provider's actual command capability before I/O. Deriving from
/// DbCommand alone does not prove async support. No synchronous fallback is permitted.
/// </remarks>
internal interface IAsyncDatabaseAccess
{
    // I/O-free validation lets deferred orchestration validate before cancellation/admission.
    void ValidateReader(IDbCommand command);
    void ValidateCommand(IDbCommand command, AsyncCommandKind kind);
    Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken);
    Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken);
    Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken cancellationToken);
}
