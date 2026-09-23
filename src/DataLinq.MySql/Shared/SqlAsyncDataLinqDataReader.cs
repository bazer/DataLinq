using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Metadata;
using MySqlConnector;

namespace DataLinq.MySql;

/// <summary>Native reader; a supplied standalone connection is owned, transaction connections stay borrowed.</summary>
internal sealed class SqlAsyncDataLinqDataReader(MySqlDataReader native, MySqlConnection? connection,
    DatabaseType? databaseType, string? providerInstanceId) : IAsyncDataReader, IDataLinqOwnedBinaryBufferReader
{
    private readonly SqlDataLinqDataReader values = new(native, databaseType);
    private readonly EnumeratorCallGate calls = new();
    private bool disposed;

    private SqlDataLinqDataReader Values
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return values; }
    }

    public object GetValue(int ordinal) => Values.GetValue(ordinal);
    public int GetOrdinal(string name) => Values.GetOrdinal(name);
    public string GetString(int ordinal) => Values.GetString(ordinal);
    public bool GetBoolean(int ordinal) => Values.GetBoolean(ordinal);
    public int GetInt32(int ordinal) => Values.GetInt32(ordinal);
    public DateOnly GetDateOnly(int ordinal) => Values.GetDateOnly(ordinal);
    public Guid GetGuid(int ordinal) => Values.GetGuid(ordinal);
    public byte[]? GetBytes(int ordinal) => Values.GetBytes(ordinal);
    public long GetBytes(int ordinal, Span<byte> buffer) => Values.GetBytes(ordinal, buffer);
    public byte[]? TakeOwnedBytes(int ordinal) => Values.GetBytes(ordinal);
    public T? GetValue<T>(ColumnDefinition column) => Values.GetValue<T>(column);
    public T? GetValue<T>(ColumnDefinition column, int ordinal) => Values.GetValue<T>(column, ordinal);
    public bool IsDbNull(int ordinal) => Values.IsDbNull(ordinal);

    public bool ReadNextRow()
    {
        using var call = calls.Enter();
        return Values.ReadNextRow();
    }

    public async Task<bool> ReadNextRowAsync(CancellationToken token)
    {
        using var call = calls.Enter();
        _ = Values;
        token.ThrowIfCancellationRequested();
        return await native.ReadAsync(token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        using var call = calls.Enter();
        if (disposed) return;
        disposed = true;
        using var diagnostics = ExecutionFailureScope.Begin();
        ExecutionFailures? failures = null;
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { native.Dispose(); }
        catch (Exception failure) { AddCleanup(ref failures, failure, occurrence); }
        occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { connection?.Dispose(); }
        catch (Exception failure) { AddCleanup(ref failures, failure, occurrence); }
        Report(failures);
    }

    public async ValueTask DisposeAsync()
    {
        using var call = calls.Enter();
        if (disposed) return;
        disposed = true;
        using var diagnostics = ExecutionFailureScope.Begin();
        ExecutionFailures? failures = null;
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { await native.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { AddCleanup(ref failures, failure, occurrence); }
        occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { AddCleanup(ref failures, failure, occurrence); }
        Report(failures);
    }

    private static void AddCleanup(ref ExecutionFailures? failures, Exception failure, long occurrence)
    {
        ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
        (failures ??= new()).AddCleanup(failure);
    }

    private void Report(ExecutionFailures? failures)
    {
        if (failures?.Primary is not { } primary) return;
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
            ExecutionRecoveryActions.None, null, ExecutionOperationKind.Unknown,
            providerInstanceId, providerIdentityIsAuthoritative: true));
        failures.ThrowIfAny();
    }
}
