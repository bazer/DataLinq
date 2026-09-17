using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledSqlReaderFactory : IAsyncSqlReaderFactory
{
    internal List<CapturedSql> Inputs { get; } = [];
    internal List<ControlledOwnedCommandFactory> Commands { get; } = [];
    internal List<ControlledAsyncDatabaseAccess> Accesses { get; } = [];
    internal Func<CapturedSql, ControlledAsyncDatabaseAccess> CreateAccess { get; set; } = _ => new();
    internal Action<ControlledOwnedCommandFactory>? ConfigureCommand { get; set; }
    internal Func<IAsyncReaderSource, IAsyncReaderSource>? WrapSource { get; set; }

    public IAsyncReaderSource BindReader(CapturedSql sql)
    {
        Inputs.Add(sql);
        var access = CreateAccess(sql);
        Accesses.Add(access);
        var factory = new ControlledOwnedCommandFactory();
        factory.Creating = () =>
        {
            factory.Resource.Borrowed.CommandText = sql.ToSql().Text;
            return factory.Resource;
        };
        ConfigureCommand?.Invoke(factory);
        Commands.Add(factory);
        var execution = new OwnedCommandExecution(access, factory);
        return WrapSource is null ? execution : WrapSource(execution);
    }
}

internal sealed class ControlledRowDataReader(params object?[][] rows) : IAsyncDataReader
{
    private int position = -1;
    internal AsyncCheckpoint Advance { get; set; } = new();
    internal AsyncCheckpoint Cleanup { get; set; } = new();
    internal Action<int>? Advancing { get; set; }
    internal int Moves { get; private set; }
    internal int Disposals { get; private set; }

    public async Task<bool> ReadNextRowAsync(CancellationToken token)
    {
        Moves++;
        Advancing?.Invoke(Moves);
        await Advance.ReachAsync(token);
        return ++position < rows.Length;
    }
    public async ValueTask DisposeAsync() { Disposals++; await Cleanup.ReachAsync(CancellationToken.None); }
    public object GetValue(int ordinal) => rows[position][ordinal]!;
    public bool IsDbNull(int ordinal) => GetValue(ordinal) is null or DBNull;
    public T? GetValue<T>(ColumnDefinition column, int ordinal) => (T?)GetValue(ordinal);
    public T? GetValue<T>(ColumnDefinition column) => (T?)GetValue(column.Index);
    public int GetOrdinal(string name) => throw new NotSupportedException();
    public string GetString(int ordinal) => (string)GetValue(ordinal);
    public int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public DateOnly GetDateOnly(int ordinal) => (DateOnly)GetValue(ordinal);
    public Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public byte[]? GetBytes(int ordinal) => (byte[]?)GetValue(ordinal);
    public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
    public bool ReadNextRow() => throw new InvalidOperationException("Unexpected synchronous read.");
    public void Dispose() => throw new InvalidOperationException("Unexpected synchronous reader cleanup.");
}
