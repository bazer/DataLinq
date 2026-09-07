using System;
using System.Data;
using System.Runtime.ExceptionServices;
using System.Threading;
using DataLinq.Metadata;

namespace DataLinq;

/// <summary>Transfers an internally created command's lifetime to its reader.</summary>
internal class OwnedCommandDataReader(IDataLinqDataReader reader, IDbCommand command) : IDataLinqDataReader
{
    private int disposed;

    internal static IDataLinqDataReader Create(IDataLinqDataReader reader, IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader is IDataLinqOwnedBinaryBufferReader ownedBinary
            ? new OwnedBinaryCommandDataReader(reader, command, ownedBinary)
            : new OwnedCommandDataReader(reader, command);
    }

    public object GetValue(int ordinal) => reader.GetValue(ordinal);
    public int GetOrdinal(string name) => reader.GetOrdinal(name);
    public string GetString(int ordinal) => reader.GetString(ordinal);
    public bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
    public int GetInt32(int ordinal) => reader.GetInt32(ordinal);
    public DateOnly GetDateOnly(int ordinal) => reader.GetDateOnly(ordinal);
    public Guid GetGuid(int ordinal) => reader.GetGuid(ordinal);
    public byte[]? GetBytes(int ordinal) => reader.GetBytes(ordinal);
    public long GetBytes(int ordinal, Span<byte> buffer) => reader.GetBytes(ordinal, buffer);
    public T? GetValue<T>(ColumnDefinition column) => reader.GetValue<T>(column);
    public T? GetValue<T>(ColumnDefinition column, int ordinal) => reader.GetValue<T>(column, ordinal);
    public bool ReadNextRow() => reader.ReadNextRow();
    public bool IsDbNull(int ordinal) => reader.IsDbNull(ordinal);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Exception? readerFailure = null;
        try
        {
            reader.Dispose();
        }
        catch (Exception exception)
        {
            readerFailure = exception;
        }

        try
        {
            command.Dispose();
        }
        catch (Exception commandFailure) when (readerFailure is not null)
        {
            throw new AggregateException("Reader and owned command disposal both failed.", readerFailure, commandFailure);
        }

        if (readerFailure is not null)
            ExceptionDispatchInfo.Capture(readerFailure).Throw();
    }

    // Preserve the optional ownership SPI only when the underlying reader offers it.
    private sealed class OwnedBinaryCommandDataReader(
        IDataLinqDataReader reader,
        IDbCommand command,
        IDataLinqOwnedBinaryBufferReader ownedBinary)
        : OwnedCommandDataReader(reader, command), IDataLinqOwnedBinaryBufferReader
    {
        public byte[]? TakeOwnedBytes(int ordinal) => ownedBinary.TakeOwnedBytes(ordinal);
    }
}
