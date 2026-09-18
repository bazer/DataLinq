using System;
using System.Data;
using System.Threading;
using DataLinq.Execution;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    internal ISyncRawModelReaderSource CaptureRawModelReader(string query, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateCommandOwner(owner);
        return BindRawModelReaderCore(query);
    }

    internal ISyncRawModelReaderSource CaptureRawModelReader(IDbCommand command, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandOwner(owner);
        return BindRawModelReaderCore(command);
    }

    // Default binding preserves legacy public overrides through the private dispatch
    // hooks. Explicit adapters can return a SyncRawCommand with precise dispatch evidence.
    internal virtual ISyncRawModelReaderSource BindRawModelReaderCore(string query)
        => new LegacyReaderSource(this, owner => ExecuteReaderOwned(query, owner));
    internal virtual ISyncRawModelReaderSource BindRawModelReaderCore(IDbCommand command)
        => new LegacyReaderSource(this, owner => ExecuteReaderOwned(command, owner));

    private sealed class LegacyReaderSource(DatabaseAccess access,
        Func<TransactionOperationGate.Step, IDataLinqDataReader> open) : ISyncRawModelReaderSource
    {
        private int state;
        public ExecutionFailureStage Stage { get; private set; } = ExecutionFailureStage.Validation;
        public void Reserve()
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                throw new InvalidOperationException("This captured raw reader has already started.");
        }
        public IDataLinqDataReader OpenReader(TransactionOperationGate.Step owner)
        {
            if (Interlocked.CompareExchange(ref state, 2, 1) != 1)
                throw new InvalidOperationException("Raw reader dispatch requires its reserved invocation.");
            Stage = ExecutionFailureStage.CommandExecution;
            return open(owner);
        }
        public ExecutionFailures? Dispose(IDataLinqDataReader? reader, ExecutionFailures? failures)
            => SyncReaderCleanup.Dispose(reader, failures);
        public ReadFailureEvidence GetFailureEvidence(Exception failure)
        {
            var evidence = access is IAsyncReadFailureEvidence classifier
                ? classifier.GetReadFailureEvidence(failure) ?? throw new InvalidOperationException("The provider returned no failure evidence.")
                : new();
            // A legacy entry is opaque once called: do not infer harmless effects from
            // SQL or exception type, nor weaken stronger failed-initialization evidence.
            return evidence.Effects == ExecutionEffects.Initialization ? evidence : evidence with { Effects = ExecutionEffects.Unknown };
        }
    }
}
