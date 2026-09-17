using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Metadata;

namespace DataLinq.Execution;

/// <summary>One reader and its DataLinq-created command; neither owns the enclosing transaction.</summary>
internal class OwnedAsyncDataReader : IAsyncDataReader
{
    private readonly IAsyncDataReader reader;
    private readonly IAsyncOwnedCommand command;
    private readonly uint? transactionId;
    private readonly EnumeratorCallGate calls = new();
    private bool disposed;

    private OwnedAsyncDataReader(IAsyncDataReader reader, IAsyncOwnedCommand command, uint? transactionId)
    {
        this.reader = reader;
        this.command = command;
        this.transactionId = transactionId;
    }

    internal static IAsyncDataReader Create(IAsyncDataReader reader, IAsyncOwnedCommand command, uint? transactionId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(command);
        return reader is IDataLinqOwnedBinaryBufferReader owned
            ? new OwnedBinaryReader(reader, command, transactionId, owned)
            : new OwnedAsyncDataReader(reader, command, transactionId);
    }

    private IAsyncDataReader CurrentReader
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return reader; }
    }

    public object GetValue(int ordinal) => CurrentReader.GetValue(ordinal);
    public int GetOrdinal(string name) => CurrentReader.GetOrdinal(name);
    public string GetString(int ordinal) => CurrentReader.GetString(ordinal);
    public bool GetBoolean(int ordinal) => CurrentReader.GetBoolean(ordinal);
    public int GetInt32(int ordinal) => CurrentReader.GetInt32(ordinal);
    public DateOnly GetDateOnly(int ordinal) => CurrentReader.GetDateOnly(ordinal);
    public Guid GetGuid(int ordinal) => CurrentReader.GetGuid(ordinal);
    public byte[]? GetBytes(int ordinal) => CurrentReader.GetBytes(ordinal);
    public long GetBytes(int ordinal, Span<byte> buffer) => CurrentReader.GetBytes(ordinal, buffer);
    public T? GetValue<T>(ColumnDefinition column) => CurrentReader.GetValue<T>(column);
    public T? GetValue<T>(ColumnDefinition column, int ordinal) => CurrentReader.GetValue<T>(column, ordinal);
    public bool IsDbNull(int ordinal) => CurrentReader.IsDbNull(ordinal);

    public bool ReadNextRow()
    {
        using var call = calls.Enter();
        return CurrentReader.ReadNextRow();
    }

    public Task<bool> ReadNextRowAsync(CancellationToken token) => ReadAsync(calls.Enter(), token);

    private async Task<bool> ReadAsync(EnumeratorCallGate.Call call, CancellationToken token)
    {
        using (call) return await CurrentReader.ReadNextRowAsync(token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        using var call = calls.Enter();
        if (disposed) return;
        disposed = true;
        AsyncCommandCleanup.Dispose(reader, command, transactionId);
    }

    public ValueTask DisposeAsync() => DisposeAsync(calls.Enter());

    private async ValueTask DisposeAsync(EnumeratorCallGate.Call call)
    {
        using (call)
        {
            if (disposed) return;
            disposed = true;
            await AsyncCommandCleanup.DisposeAsync(reader, command, transactionId).ConfigureAwait(false);
        }
    }

    private sealed class OwnedBinaryReader(IAsyncDataReader reader, IAsyncOwnedCommand command, uint? transactionId,
        IDataLinqOwnedBinaryBufferReader owned) : OwnedAsyncDataReader(reader, command, transactionId), IDataLinqOwnedBinaryBufferReader
    {
        public byte[]? TakeOwnedBytes(int ordinal)
        {
            _ = CurrentReader;
            return owned.TakeOwnedBytes(ordinal);
        }
    }
}
