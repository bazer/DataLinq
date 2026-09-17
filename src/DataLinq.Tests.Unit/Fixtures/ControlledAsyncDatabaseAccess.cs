using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Fixtures;

/// <summary>Deterministic pause/failure point; no sleeps, worker scheduling, or database required.</summary>
internal sealed class AsyncCheckpoint
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal AsyncCheckpoint(bool paused = false)
    {
        if (!paused)
            Release();
    }

    internal CancellationToken ObservedToken { get; private set; }
    internal Task Entered => entered.Task;

    internal async Task ReachAsync(CancellationToken cancellationToken)
    {
        ObservedToken = cancellationToken;
        entered.TrySetResult();
        await resumed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void Release() => resumed.TrySetResult();
    internal void Fail(Exception exception) => resumed.TrySetException(exception);
}

/// <summary>
/// Implements only the internal execution capability. It does not construct a DataLinq
/// database, connect to SQLite, or claim native-provider/caching/transaction fidelity.
/// </summary>
internal sealed class ControlledAsyncDatabaseAccess(AsyncCheckpoint? dispatch = null) : AsyncDatabaseAccess
{
    internal AsyncCheckpoint Dispatch { get; } = dispatch ?? new AsyncCheckpoint();
    internal ConcurrentQueue<string> Calls { get; } = new();
    internal ControlledAsyncDataReader Reader { get; set; } = new();
    internal Exception? ValidationFailure { get; set; }
    internal AsyncCommandKind? UnsupportedKind { get; set; }
    internal IDbCommand? ObservedCommand { get; private set; }
    internal object? ScalarResult { get; set; }
    internal int NonQueryResult { get; set; }

    protected override void ValidateCommand(IDbCommand command, AsyncCommandKind kind)
    {
        Calls.Enqueue($"validate:{kind}");
        if (ValidationFailure is not null)
            throw ValidationFailure;
        if (command is not ControlledCommand || kind == UnsupportedKind)
            throw new NotSupportedException("The controlled provider does not support this command/operation.");
    }

    private Task DispatchAsync(IDbCommand command, AsyncCommandKind kind, CancellationToken cancellationToken)
    {
        ObservedCommand = command;
        Calls.Enqueue($"dispatch:{kind}");
        return Dispatch.ReachAsync(cancellationToken);
    }

    protected override async Task<IAsyncDataReader> ExecuteReaderCoreAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        await DispatchAsync(command, AsyncCommandKind.Reader, cancellationToken).ConfigureAwait(false);
        return Reader;
    }

    protected override async Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        await DispatchAsync(command, AsyncCommandKind.Scalar, cancellationToken).ConfigureAwait(false);
        return ScalarResult;
    }

    protected override async Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        await DispatchAsync(command, AsyncCommandKind.NonQuery, cancellationToken).ConfigureAwait(false);
        return NonQueryResult;
    }
}

internal sealed class ControlledAsyncDataReader : IAsyncDataReader
{
    private readonly int[] rows = [11, 22];
    private int position = -1;

    internal AsyncCheckpoint Advance { get; set; } = new();
    internal AsyncCheckpoint Cleanup { get; set; } = new();
    internal int AsyncDisposeCalls { get; private set; }
    internal int SyncCalls { get; private set; }
    internal bool IsDisposed { get; private set; }

    public async Task<bool> ReadNextRowAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await Advance.ReachAsync(cancellationToken).ConfigureAwait(false);
        return ++position < rows.Length;
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
            return;
        AsyncDisposeCalls++;
        await Cleanup.ReachAsync(CancellationToken.None).ConfigureAwait(false);
        IsDisposed = true;
    }

    public void Dispose()
    {
        SyncCalls++;
        throw new InvalidOperationException("Unexpected synchronous reader disposal.");
    }

    public bool ReadNextRow()
    {
        SyncCalls++;
        throw new InvalidOperationException("Unexpected synchronous reader advancement.");
    }

    public int GetInt32(int ordinal)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ordinal != 0 || position < 0 || position >= rows.Length)
            throw new InvalidOperationException("There is no current value at this ordinal.");
        return rows[position];
    }

    public object GetValue(int ordinal) => GetInt32(ordinal);
    public int GetOrdinal(string name) => name == "Value" ? 0 : throw new IndexOutOfRangeException(name);
    public bool IsDbNull(int ordinal) { _ = GetInt32(ordinal); return false; }
    public string GetString(int ordinal) => throw new NotSupportedException();
    public bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
    public Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();
    public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
    public T? GetValue<T>(ColumnDefinition column) => throw new NotSupportedException();
    public T? GetValue<T>(ColumnDefinition column, int ordinal) => throw new NotSupportedException();
}

/// <summary>
/// Intentionally derives from DbCommand without overriding its async defaults. Dispatch
/// must go through the explicitly implemented provider capability, never those defaults.
/// </summary>
internal abstract class ProbeDbCommand : DbCommand
{
    internal int SyncExecutionCalls { get; private set; }
    internal int DisposeCalls { get; private set; }
    [AllowNull] public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
    public override void Cancel() => throw new NotSupportedException();
    public override void Prepare() => throw new NotSupportedException();
    public override int ExecuteNonQuery() => UnexpectedSync<int>();
    public override object? ExecuteScalar() => UnexpectedSync<object?>();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => UnexpectedSync<DbDataReader>();

    private T UnexpectedSync<T>()
    {
        SyncExecutionCalls++;
        throw new InvalidOperationException("Unexpected synchronous command dispatch.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeCalls++;
        base.Dispose(disposing);
    }
}

internal sealed class ControlledCommand : ProbeDbCommand;
internal sealed class UnverifiedCommand : ProbeDbCommand;
