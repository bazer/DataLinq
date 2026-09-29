using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Metadata;

namespace DataLinq.Execution;

/// <summary>A current-row view with no authority to advance or release its sequence's reader.</summary>
internal sealed class BorrowedAsyncDataReader(IAsyncDataReader reader, Func<IDisposable> enter) : IAsyncDataReader
{
    private static InvalidOperationException OwnershipError() => new("The sequence owns reader advancement and disposal.");
    public bool ReadNextRow() => throw OwnershipError();
    public Task<bool> ReadNextRowAsync(CancellationToken cancellationToken) => throw OwnershipError();
    public void Dispose() => throw OwnershipError();
    public ValueTask DisposeAsync() => throw OwnershipError();

    public object GetValue(int ordinal) { using var call = enter(); return reader.GetValue(ordinal); }
    public int GetOrdinal(string name) { using var call = enter(); return reader.GetOrdinal(name); }
    public string GetString(int ordinal) { using var call = enter(); return reader.GetString(ordinal); }
    public bool GetBoolean(int ordinal) { using var call = enter(); return reader.GetBoolean(ordinal); }
    public int GetInt32(int ordinal) { using var call = enter(); return reader.GetInt32(ordinal); }
    public DateOnly GetDateOnly(int ordinal) { using var call = enter(); return reader.GetDateOnly(ordinal); }
    public Guid GetGuid(int ordinal) { using var call = enter(); return reader.GetGuid(ordinal); }
    public byte[]? GetBytes(int ordinal) { using var call = enter(); return reader.GetBytes(ordinal); }
    public long GetBytes(int ordinal, Span<byte> buffer) { using var call = enter(); return reader.GetBytes(ordinal, buffer); }
    public T? GetValue<T>(ColumnDefinition column) { using var call = enter(); return reader.GetValue<T>(column); }
    public T? GetValue<T>(ColumnDefinition column, int ordinal) { using var call = enter(); return reader.GetValue<T>(column, ordinal); }
    public bool IsDbNull(int ordinal) { using var call = enter(); return reader.IsDbNull(ordinal); }
}
