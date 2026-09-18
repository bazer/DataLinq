using System;
using System.Data;

namespace DataLinq.Execution;

/// <summary>
/// I/O-free binding for a managed read of a caller-owned command. The bound source
/// preserves command identity and may compose private transaction initialization;
/// it must never snapshot, mutate or take ownership of the command.
/// </summary>
internal interface IAsyncBorrowedReaderFactory
{
    IAsyncReaderSource BindBorrowedReader(IDbCommand command);

    internal static IAsyncBorrowedReaderFactory Require(object access) => access as IAsyncBorrowedReaderFactory
        ?? throw new NotSupportedException("This database access does not support managed asynchronous borrowed-command reads.");
}
