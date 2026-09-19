using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>
/// Caller-owned raw reader. Admission starts before acquisition and ends only after cleanup,
/// including EOF/paused rows. A helper can close admission and drain an escaped reader.
/// </summary>
internal sealed class AsyncRawDataReader : IAsyncDataReader, IHelperTrackedReader
{
    private const string Operation = "use an asynchronous raw reader";
    private readonly IAsyncReaderSource source;
    private readonly Transaction? transaction;
    private readonly EnumeratorCallGate calls = new();
    private TransactionReadScope? ownership;
    private IAsyncDataReader? reader;
    private bool finished;
    private bool hasCurrent;
    private bool helperDrained;

    private AsyncRawDataReader(IAsyncReaderSource source, Transaction? transaction)
    {
        this.source = source;
        this.transaction = transaction;
    }

    internal static async Task<IAsyncDataReader> OpenAsync(IAsyncReaderSource source, Transaction? transaction, CancellationToken token)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ArgumentNullException.ThrowIfNull(source);
        transaction?.EnsureCanRead(Operation, operationKind: ExecutionOperationKind.RawCommand);
        source.Validate();
        if (source is IAsyncTransactionReaderSource && transaction is null)
            throw new InvalidOperationException("This reader requires a managed transaction owner.");
        token.ThrowIfCancellationRequested();
        var result = new AsyncRawDataReader(source, transaction);
        using var call = result.calls.Enter();
        ExecutionFailures failures;
        try
        {
            result.ownership = transaction is null ? null : DataSourceAccess.BeginRead(transaction, Operation, cancellationToken: token,
                operationKind: ExecutionOperationKind.RawCommand);
            result.ownership?.RegisterReader(result);
            result.reader = await (source is IAsyncTransactionReaderSource owned
                ? owned.OpenReaderAsync(result.ownership!.Step, token) : source.OpenReaderAsync(token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
            // Ownership must reach the caller even if the provider completed despite a
            // late cancellation request. Subsequent reads have their own call tokens.
            return result;
        }
        catch (Exception failure) { failures = new(); Capture(failures, failure, ExecutionFailureStage.CommandExecution, token); }
        await result.FinishAsync(failures).ConfigureAwait(false);
        failures.ThrowIfAny();
        throw new InvalidOperationException("Failed reader acquisition did not report its failure.");
    }

    public Task<bool> ReadNextRowAsync(CancellationToken cancellationToken) => ReadAsync(calls.Enter(), cancellationToken);

    private async Task<bool> ReadAsync(EnumeratorCallGate.Call call, CancellationToken token)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using (call)
        {
            // A stale/overlapping call must not run cleanup or replace an earlier failure.
            Validate();
            hasCurrent = false;
            ExecutionFailures failures;
            try
            {
                token.ThrowIfCancellationRequested();
                var next = await reader!.ReadNextRowAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return hasCurrent = next;
            }
            catch (Exception failure) { failures = new(); Capture(failures, failure, ExecutionFailureStage.RowLoading, token); }
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
            return false;
        }
    }

    public bool ReadNextRow()
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using var call = calls.Enter();
        Validate();
        hasCurrent = false;
        ExecutionFailures failures;
        try { return hasCurrent = reader!.ReadNextRow(); }
        catch (Exception failure) { failures = new(); Capture(failures, failure, ExecutionFailureStage.RowLoading, default); }
        Finish(failures);
        failures.ThrowIfAny();
        return false;
    }

    public void Dispose()
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        if (Volatile.Read(ref helperDrained)) return;
        using var call = calls.Enter();
        var failures = new ExecutionFailures();
        Finish(failures);
        failures.ThrowIfAny();
    }

    public ValueTask DisposeAsync() => Volatile.Read(ref helperDrained) ? ValueTask.CompletedTask : DisposeAsync(calls.Enter());

    private async ValueTask DisposeAsync(EnumeratorCallGate.Call call)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using (call)
        {
            var failures = new ExecutionFailures();
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
        }
    }

    public void StopAdmission() => calls.StopAdmission();
    public async ValueTask DrainAsync()
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        await calls.WaitForIdleAsync().ConfigureAwait(false);
        try
        {
            var failures = new ExecutionFailures();
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
        }
        finally { Volatile.Write(ref helperDrained, true); }
    }

    private IAsyncDataReader? TakeReader()
    {
        finished = true;
        hasCurrent = false;
        var owned = reader;
        reader = null;
        return owned;
    }

    private async ValueTask FinishAsync(ExecutionFailures failures)
    {
        if (finished) return;
        var owned = TakeReader();
        try { if (owned is not null) await owned.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup) { failures.AddCleanup(cleanup); }
        finally { PublishAndRelease(failures); }
    }

    private void Finish(ExecutionFailures failures)
    {
        if (finished) return;
        var owned = TakeReader();
        try { owned?.Dispose(); }
        catch (Exception cleanup) { failures.AddCleanup(cleanup); }
        finally { PublishAndRelease(failures); }
    }

    private void PublishAndRelease(ExecutionFailures failures)
    {
        try
        {
            if (failures.Primary is not { } failure) return;
            var evidence = new ReadFailureEvidence();
            var assessed = true;
            try
            {
                if (source is IAsyncReadFailureEvidence classifier)
                    evidence = classifier.GetReadFailureEvidence(failure)
                        ?? throw new InvalidOperationException("The provider returned no failure evidence.");
            }
            catch (Exception assessment)
            {
                assessed = false;
                failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
            }
            var recovery = ownership is null ? ExecutionRecoveryActions.None
                : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessed);
            var context = failures.Snapshot(evidence, transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                recovery, transaction?.TransactionID, ExecutionOperationKind.RawCommand, transaction?.ExecutionGate.ProviderInstanceId);
            if (ownership is not null) transaction!.RecordAsyncReadFailure(ownership.Step, context);
            ExecutionFailureContexts.Attach(failure, context);
            ownership?.ReportFailure(failure);
        }
        finally
        {
            ownership?.Dispose();
            ownership = null;
        }
    }

    private static void Capture(ExecutionFailures failures, Exception failure, ExecutionFailureStage stage, CancellationToken token)
        => failures.AddReported(failure, stage, failure is OperationCanceledException canceled &&
            canceled.CancellationToken == token && token.IsCancellationRequested ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);

    private void Validate()
    {
        ObjectDisposedException.ThrowIf(finished, this);
        transaction?.EnsureCanRead(Operation, ownership?.Step);
    }

    // Getters inspect already available row data; they neither advance nor release the
    // reader. Local invalid-column/type usage remains the caller's materialization work.
    private EnumeratorCallGate.Call EnterCurrent(bool requireRow = true)
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
}
