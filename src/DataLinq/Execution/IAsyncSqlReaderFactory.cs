using System;

namespace DataLinq.Execution;

/// <summary>
/// Explicit source-bound capability for captured SQL reads. Binding is I/O-free and transfers
/// no live resources. The returned source owns commands it creates, validates before cancellation,
/// and retains the selected source/initialization policy through its reader lifetime.
/// </summary>
internal interface IAsyncSqlReaderFactory
{
    // A multi-command invocation must not rediscover mutable adapter policy after
    // suspension. The snapshot may bind later SQL derived from earlier results.
    IAsyncSqlReaderFactory CaptureInvocation();

    IAsyncReaderSource BindReader(CapturedSql sql);

    internal static IAsyncSqlReaderFactory Require(object access) => access as IAsyncSqlReaderFactory
        ?? throw new NotSupportedException("This database access does not implement captured asynchronous SQL reads.");
}
