using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    internal Task<IAsyncDataReader> ExecuteReaderAsyncCore(string query, CancellationToken cancellationToken = default)
        => AsyncRawDataReader.OpenAsync(CaptureRawReader(query), managedTransaction, cancellationToken, DiagnosticProviderInstanceId, StandaloneReaderOwner);

    internal Task<IAsyncDataReader> ExecuteReaderAsyncCore(IDbCommand command, CancellationToken cancellationToken = default)
        => AsyncRawDataReader.OpenAsync(CaptureRawReader(command), managedTransaction, cancellationToken, DiagnosticProviderInstanceId, StandaloneReaderOwner);

    internal IAsyncEnumerable<IDataLinqDataReader> ReadReaderAsyncCore(string query, CancellationToken cancellationToken = default)
        => new AsyncReaderEnumerable<IDataLinqDataReader>(() => CaptureRawReader(query), static reader => reader, managedTransaction, cancellationToken,
            new(ExecutionOperationKind.RawCommand, DiagnosticProviderInstanceId), StandaloneReaderOwner, borrowCurrentRow: true);

    internal IAsyncEnumerable<IDataLinqDataReader> ReadReaderAsyncCore(IDbCommand command, CancellationToken cancellationToken = default)
        => new AsyncReaderEnumerable<IDataLinqDataReader>(() => CaptureRawReader(command), static reader => reader, managedTransaction, cancellationToken,
            new(ExecutionOperationKind.RawCommand, DiagnosticProviderInstanceId), StandaloneReaderOwner, borrowCurrentRow: true);

    private DatabaseTransaction? StandaloneReaderOwner => managedTransaction is null ? this as DatabaseTransaction : null;

    private void ValidateRawReaderOwner()
    {
        StandaloneReaderOwner?.EnsureStandaloneCommandAllowed();
        managedTransaction?.EnsureCanRead("execute an asynchronous raw reader", operationKind: ExecutionOperationKind.RawCommand);
    }

    private IAsyncReaderSource CaptureRawReader(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateRawReaderOwner();
        return RawAsyncReaderSource.Wrap(IAsyncSqlReaderFactory.Require(this).BindReader(CapturedSql.Capture(new Sql(query))));
    }

    private IAsyncReaderSource CaptureRawReader(IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateRawReaderOwner();
        return RawAsyncReaderSource.Wrap(IAsyncBorrowedReaderFactory.Require(this).BindBorrowedReader(command));
    }

    // The enclosing managed reader operation owns admission and cleanup. Returning its
    // native reader does not transfer or release the enclosing private step.
    internal async Task<IAsyncDataReader> ExecuteReaderOwnedAsyncCore(IDbCommand command,
        TransactionOperationGate.Step owner, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(owner);
        if (managedTransaction is null)
            throw new InvalidOperationException("Private reader dispatch requires its managed transaction owner.");
        managedTransaction.EnsureCanRead("execute an asynchronous reader", owner);
        var source = IAsyncBorrowedReaderFactory.Require(this).BindBorrowedReader(command);
        source.Validate();
        token.ThrowIfCancellationRequested();
        return await (source is IAsyncTransactionReaderSource owned
            ? owned.OpenReaderAsync(owner, token) : source.OpenReaderAsync(token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
    }
}
