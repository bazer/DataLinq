using System;
using System.Data;

namespace DataLinq.Execution;

/// <summary>
/// Explicit ownership/cleanup capability for a DataLinq-created command. Neither DbCommand
/// inheritance nor IAsyncDisposable alone proves supported provider dispatch or disposal.
/// Both disposal paths release only this command, never its surrounding transaction.
/// </summary>
internal interface IAsyncOwnedCommand : IDisposable, IAsyncDisposable
{
    IDbCommand Command { get; }
}

/// <summary>
/// Bound to captured invocation inputs and the selected provider. Validation and construction
/// are I/O-free; validation checks lifecycle, input shape and operation/cleanup capabilities
/// before cancellation. Create transfers one command or cleans up its own partial construction
/// before throwing. It must not reopen a connection or read a caller's live mutable builder.
/// Native implementations and complete query capture are separate integrations.
/// </summary>
internal interface IAsyncOwnedCommandFactory
{
    void Validate(AsyncCommandKind kind);
    IAsyncOwnedCommand Create();
}
