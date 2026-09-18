using System;
using System.Collections.Generic;
using System.Data;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledEagerCommandFactory : IAsyncEagerCommandFactory
{
    internal ControlledAsyncDatabaseAccess Access { get; set; } = new();
    internal IAsyncCommandInitialization? Initialization { get; set; }
    internal Action<ControlledOwnedCommandFactory>? ConfigureCommand { get; set; }
    internal Func<object?, object?> ScalarConverting { get; set; } = value => value;
    internal List<ControlledOwnedCommandFactory> Commands { get; } = [];
    internal List<IDbCommand> Borrowed { get; } = [];

    public AsyncEagerCommand BindCommand(string sql)
    {
        var factory = new ControlledOwnedCommandFactory();
        factory.Creating = () => { factory.Resource.Borrowed.CommandText = sql; return factory.Resource; };
        ConfigureCommand?.Invoke(factory);
        Commands.Add(factory);
        return new(Access, factory, Initialization);
    }

    public AsyncEagerCommand BindCommand(IDbCommand command)
    {
        Borrowed.Add(command);
        return new(Access, command, Initialization);
    }

    public AsyncEagerScalarInvocation<T> BindScalar<T>(string sql)
    {
        var convert = ScalarConverting;
        return new(BindCommand(sql), value => (T)convert(value)!);
    }

    public AsyncEagerScalarInvocation<T> BindScalar<T>(IDbCommand command)
    {
        var convert = ScalarConverting;
        return new(BindCommand(command), value => (T)convert(value)!);
    }
}
