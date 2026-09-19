using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>Direct synchronous raw reader; holds admission through EOF and owned cleanup.</summary>
internal class SyncRawDataReader : IDataLinqDataReader, IHelperTrackedReader
{
    private const string Operation = "use a synchronous raw reader";
    private readonly SyncRawCommand command;
    private readonly Transaction? transaction;
    private readonly EnumeratorCallGate calls = new();
    private TransactionReadScope? ownership;
    private IDataLinqDataReader? reader;
    private bool finished;
    private bool hasCurrent;
    private bool helperDrained;

    private SyncRawDataReader(SyncRawCommand command, Transaction? transaction,
        TransactionReadScope? ownership, IDataLinqDataReader reader)
    {
        this.command = command;
        this.transaction = transaction;
        this.ownership = ownership;
        this.reader = reader;
    }

    internal static IDataLinqDataReader Open(SyncRawCommand command, Transaction? transaction)
    {
        transaction?.EnsureCanRead(Operation, operationKind: ExecutionOperationKind.RawCommand);
        command.Reserve(SyncCommandKind.Reader, transaction is not null);
        var ownership = transaction is null ? null : DataSourceAccess.BeginRead(transaction, Operation, operationKind: ExecutionOperationKind.RawCommand);
        IDataLinqDataReader? reader = null;
        ExecutionFailures failures;
        try
        {
            // Until acquisition settles, the active lease makes helpers wait. Register
            // the acquired reader before returning, so a closing helper can drain it.
            reader = command.ExecuteReader(ownership?.Step);
            SyncRawDataReader result = reader is IDataLinqOwnedBinaryBufferReader binary
                ? new OwnedBinaryReader(command, transaction, ownership, reader, binary)
                : new SyncRawDataReader(command, transaction, ownership, reader);
            using var call = result.calls.Enter();
            ownership?.RegisterReader(result);
            return result;
        }
        catch (Exception failure) { (failures = new()).AddReported(failure, command.Stage); }
        try { reader?.Dispose(); }
        catch (Exception cleanup) { failures.AddCleanup(cleanup); }
        failures = command.DisposeOwnedCommand(failures)!;
        try { SyncRawExecution.PublishFailure(command, failures, transaction, ownership); }
        finally { ownership?.Dispose(); }
        failures.ThrowIfAny();
        throw new InvalidOperationException("Failed reader acquisition did not report its failure.");
    }

    public bool ReadNextRow()
    {
        using var call = calls.Enter();
        Validate();
        hasCurrent = false;
        ExecutionFailures failures;
        try { return hasCurrent = reader!.ReadNextRow(); }
        catch (Exception failure) { (failures = new()).AddReported(failure, ExecutionFailureStage.RowLoading); }
        Finish(failures)?.ThrowIfAny();
        return false;
    }

    public void Dispose()
    {
        if (Volatile.Read(ref helperDrained)) return;
        using var call = calls.Enter();
        Finish(null)?.ThrowIfAny();
    }

    public void StopAdmission() => calls.StopAdmission();
    public async ValueTask DrainAsync()
    {
        await calls.WaitForIdleAsync().ConfigureAwait(false);
        // This resource came from the synchronous boundary. Drain its actual sync
        // cleanup directly, even when an async helper owns transaction completion.
        try { Finish(null)?.ThrowIfAny(); }
        finally { Volatile.Write(ref helperDrained, true); }
    }

    private ExecutionFailures? Finish(ExecutionFailures? failures)
    {
        if (finished) return failures;
        finished = true;
        hasCurrent = false;
        var owned = reader;
        reader = null;
        try { owned?.Dispose(); }
        catch (Exception cleanup) { (failures ??= new()).AddCleanup(cleanup); }
        failures = command.DisposeOwnedCommand(failures);
        try { SyncRawExecution.PublishFailure(command, failures, transaction, ownership); }
        finally
        {
            ownership?.Dispose();
            ownership = null;
        }
        return failures;
    }

    private void Validate()
    {
        ObjectDisposedException.ThrowIf(finished, this);
        transaction?.EnsureCanRead(Operation, ownership?.Step);
    }

    protected EnumeratorCallGate.Call EnterCurrent(bool requireRow = true)
    {
        var call = calls.Enter();
        try
        {
            Validate();
            if (requireRow && !hasCurrent) throw new InvalidOperationException("The reader is not positioned on a row.");
            return call;
        }
        catch { call.Dispose(); throw; }
    }

    public object GetValue(int ordinal) { using var call = EnterCurrent(); return reader!.GetValue(ordinal); }
    public int GetOrdinal(string name) { using var call = EnterCurrent(false); return reader!.GetOrdinal(name); }
    public string GetString(int ordinal) { using var call = EnterCurrent(); return reader!.GetString(ordinal); }
    public bool GetBoolean(int ordinal) { using var call = EnterCurrent(); return reader!.GetBoolean(ordinal); }
    public int GetInt32(int ordinal) { using var call = EnterCurrent(); return reader!.GetInt32(ordinal); }
    public DateOnly GetDateOnly(int ordinal) { using var call = EnterCurrent(); return reader!.GetDateOnly(ordinal); }
    public Guid GetGuid(int ordinal) { using var call = EnterCurrent(); return reader!.GetGuid(ordinal); }
    public byte[]? GetBytes(int ordinal) { using var call = EnterCurrent(); return reader!.GetBytes(ordinal); }
    public long GetBytes(int ordinal, Span<byte> buffer) { using var call = EnterCurrent(); return reader!.GetBytes(ordinal, buffer); }
    public T? GetValue<T>(ColumnDefinition column) { using var call = EnterCurrent(); return reader!.GetValue<T>(column); }
    public T? GetValue<T>(ColumnDefinition column, int ordinal) { using var call = EnterCurrent(); return reader!.GetValue<T>(column, ordinal); }
    public bool IsDbNull(int ordinal) { using var call = EnterCurrent(); return reader!.IsDbNull(ordinal); }

    private sealed class OwnedBinaryReader(SyncRawCommand command, Transaction? transaction,
        TransactionReadScope? ownership, IDataLinqDataReader reader, IDataLinqOwnedBinaryBufferReader binary)
        : SyncRawDataReader(command, transaction, ownership, reader), IDataLinqOwnedBinaryBufferReader
    {
        public byte[]? TakeOwnedBytes(int ordinal)
        {
            using var call = EnterCurrent();
            return binary.TakeOwnedBytes(ordinal);
        }
    }
}
