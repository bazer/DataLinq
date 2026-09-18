using System;
using System.Collections.Generic;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledMutationCommandFactory : IAsyncMutationCommandFactory
{
    internal Func<CapturedSql, ControlledAsyncDatabaseAccess> CreateAccess { get; set; } = _ => new()
    {
        NonQueryResult = 1,
        FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true)
    };
    internal IAsyncCommandInitialization? Initialization { get; set; }
    internal Action<ControlledOwnedCommandFactory>? ConfigureCommand { get; set; }
    internal List<CapturedSql> Inputs { get; } = [];
    internal List<ControlledAsyncDatabaseAccess> Accesses { get; } = [];
    internal List<ControlledOwnedCommandFactory> Commands { get; } = [];

    public AsyncEagerCommand BindMutation(CapturedSql sql)
    {
        Inputs.Add(sql);
        var access = CreateAccess(sql);
        Accesses.Add(access);
        var command = new ControlledOwnedCommandFactory();
        command.Creating = () => { command.Resource.Borrowed.CommandText = sql.Text; return command.Resource; };
        ConfigureCommand?.Invoke(command);
        Commands.Add(command);
        return new(access, command, Initialization);
    }
}
