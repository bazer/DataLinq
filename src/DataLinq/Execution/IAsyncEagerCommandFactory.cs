using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>I/O-free, per-invocation binding of lower-level commands and scalar conversion.</summary>
internal interface IAsyncEagerCommandFactory
{
    AsyncEagerCommand BindCommand(string sql);
    AsyncEagerCommand BindCommand(IDbCommand command);
    AsyncEagerScalarInvocation<T> BindScalar<T>(string sql);
    AsyncEagerScalarInvocation<T> BindScalar<T>(IDbCommand command);

    internal static IAsyncEagerCommandFactory Require(object access) => access as IAsyncEagerCommandFactory
        ?? throw new NotSupportedException("This database access does not implement asynchronous eager command binding.");
}

internal sealed record AsyncEagerScalarInvocation<T>(AsyncEagerCommand Command, Func<object?, T> Convert);

/// <summary>Private initialization shared by command families; never opens outside an execution owner.</summary>
internal interface IAsyncCommandInitialization
{
    TransactionInitializationState State { get; }
    void Validate();
    Task InitializeAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken);
}
