using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>A captured scalar command; execution settles owned cleanup before returning.</summary>
internal interface IAsyncScalarSource
{
    void Validate();
    Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken);
}

internal interface IAsyncTransactionScalarSource : IAsyncScalarSource
{
    Task<object?> ExecuteScalarAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken);
}

internal interface IAsyncSqlScalarFactory
{
    // Binding is I/O-free and retains the selected provider/initialization policy.
    IAsyncScalarSource BindScalar(CapturedSql sql);

    // Preserve the selected provider's existing typed-scalar policy (including null).
    // This is a local conversion, never another command or synchronous I/O fallback.
    AsyncScalarInvocation<T> BindScalar<T>(CapturedSql sql);

    internal static IAsyncSqlScalarFactory Require(object access) => access as IAsyncSqlScalarFactory
        ?? throw new NotSupportedException("This database access does not implement captured asynchronous scalar reads.");
}

internal sealed record AsyncScalarInvocation<T>(IAsyncScalarSource Source, Func<object?, T> Convert);
