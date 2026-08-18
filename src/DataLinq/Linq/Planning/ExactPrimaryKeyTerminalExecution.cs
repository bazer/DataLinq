using System;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal interface IExactPrimaryKeyTerminalExecutionServices
{
    IImmutableInstance? ExecuteExactPrimaryKeyTerminal(
        TableDefinition table,
        object? canonicalProviderKey,
        QueryPlanResultKind resultKind);
}

internal static class ExactPrimaryKeyTerminalExecution
{
    internal static IImmutableInstance? ApplyResultSemantics(
        IImmutableInstance? row,
        QueryPlanResultKind resultKind)
    {
        if (row is not null)
            return row;

        return resultKind switch
        {
            QueryPlanResultKind.SingleOrDefault or QueryPlanResultKind.FirstOrDefault => null,
            QueryPlanResultKind.Single or QueryPlanResultKind.First =>
                throw new InvalidOperationException("Sequence contains no elements"),
            _ => throw new InvalidOperationException(
                $"Exact primary-key terminal execution does not support result kind '{resultKind}'.")
        };
    }
}
