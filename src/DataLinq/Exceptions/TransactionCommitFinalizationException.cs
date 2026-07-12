using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Exceptions;

/// <summary>
/// The exception thrown when the database commit succeeded but DataLinq could not complete
/// committed cache publication or local transaction finalization.
/// </summary>
public sealed class TransactionCommitFinalizationException : InvalidOperationException
{
    internal TransactionCommitFinalizationException(
        uint transactionId,
        Exception finalizationFailure,
        IReadOnlyList<Exception> cleanupFailures)
        : base(CreateMessage(transactionId, cleanupFailures.Count), finalizationFailure)
    {
        TransactionId = transactionId;
        CleanupFailures = Array.AsReadOnly(cleanupFailures.ToArray());
    }

    /// <summary>
    /// Gets the DataLinq transaction identifier whose database commit succeeded.
    /// </summary>
    public uint TransactionId { get; }

    /// <summary>
    /// Gets additional exceptions raised while DataLinq attempted conservative cache and
    /// transaction-local cleanup. The original finalization failure is available through
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    public IReadOnlyList<Exception> CleanupFailures { get; }

    private static string CreateMessage(uint transactionId, int cleanupFailureCount)
    {
        var cleanupDescription = cleanupFailureCount == 0
            ? "Transaction-local state was removed, committed caches were conservatively cleared, and transaction-derived mutable baselines were invalidated."
            : $"DataLinq attempted transaction-local removal and conservative committed-cache clearing, but that recovery reported {cleanupFailureCount} additional failure(s).";

        return
            $"Database commit for transaction {transactionId} succeeded, but DataLinq could not finalize committed cache and local transaction state. " +
            $"{cleanupDescription} Do not retry commit or report a rollback; materialize fresh committed rows before continuing.";
    }
}
