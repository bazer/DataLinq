using System;

namespace DataLinq.Execution;

/// <summary>I/O-free binding of a captured tracked mutation, including its owned command.</summary>
internal interface IAsyncMutationCommandFactory
{
    AsyncEagerCommand BindMutation(CapturedSql sql);

    internal static IAsyncMutationCommandFactory Require(object access) => access as IAsyncMutationCommandFactory
        ?? throw new NotSupportedException("This adapter does not implement captured asynchronous tracked mutations.");
}
