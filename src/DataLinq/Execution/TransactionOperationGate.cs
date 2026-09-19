using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Per-transaction, fail-fast admission. An explicit lease, never a thread or ambient
/// execution context, owns the slot. Locks protect transitions only, not provider work.
/// Lifecycle/capability validation and pre-cancellation remain the caller's responsibility.
/// </summary>
internal sealed class TransactionOperationGate(uint transactionId, string? providerInstanceId = null)
{
    private readonly object sync = new();
    internal uint TransactionId => transactionId;
    internal string? ProviderInstanceId => providerInstanceId;
    private Lease? active;
    private HelperOwner? helper;
    private TaskCompletionSource? changed;

    internal Lease Enter(string operation, bool completion = false, ExecutionOperationKind operationKind = ExecutionOperationKind.Unknown)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        lock (sync)
        {
            if (completion && helper is not null)
                throw Rejected($"Transaction {transactionId} completion is owned by its callback helper.", operationKind);
            ThrowIfBusyCore(operation, operationKind);
            return active = new Lease(this, operation, operationKind);
        }
    }

    internal void ThrowIfBusy(string operation, ExecutionOperationKind operationKind = ExecutionOperationKind.Unknown)
    {
        lock (sync)
            ThrowIfBusyCore(operation, operationKind);
    }

    // Repeated disposal after completion is harmless, but disposal still in progress
    // owns its slot even after the wrapper has already become terminal.
    internal void ThrowIfActive(string operation, ExecutionOperationKind operationKind = ExecutionOperationKind.Unknown)
    {
        lock (sync)
            if (active is not null)
                throw Rejected($"Cannot {operation} through transaction {transactionId} while '{active.Operation}' is active.", operationKind);
    }

    private void ThrowIfBusyCore(string operation, ExecutionOperationKind operationKind)
    {
        if (helper?.Closed == true)
            throw Rejected($"Cannot {operation} through transaction {transactionId} after its helper callback has ended.", operationKind);
        if (active is not null)
            throw Rejected($"Cannot {operation} through transaction {transactionId} while '{active.Operation}' is active.", operationKind);
    }

    // Called under the gate lock. Reporting copies identifiers only and never
    // publishes a failure into the admitted operation or transaction state.
    private InvalidOperationException Rejected(string message, ExecutionOperationKind operationKind)
    {
        var failure = new InvalidOperationException(message);
        ExecutionFailureContexts.Attach(failure, new(ExecutionFailureCause.InvalidOperation, ExecutionFailureStage.Validation,
            ExecutionCompletion.Unknown, active is null ? ExecutionRecoveryActions.None : ExecutionRecoveryActions.FinishActiveOperation,
            transactionId, [], operation: operationKind, providerInstanceId: providerInstanceId, activeOperation: active?.Kind));
        return failure;
    }

    internal HelperOwner BeginHelperLifetime()
    {
        lock (sync)
        {
            if (helper is not null)
                throw Rejected($"Transaction {transactionId} already belongs to a callback helper.", ExecutionOperationKind.TransactionCallback);
            ThrowIfBusyCore("begin callback helper", ExecutionOperationKind.TransactionCallback);
            return helper = new HelperOwner(this);
        }
    }

    private void SignalChange()
    {
        changed?.TrySetResult();
        changed = null;
    }

    /// <summary>Separate lifetime authority; does not occupy the callback's execution slot.</summary>
    internal sealed class HelperOwner(TransactionOperationGate gate)
    {
        internal bool Closed { get; private set; }
        private readonly List<Exception> drainedFailures = [];

        internal Task<Lease> CloseAndDrainAsync(ExecutionFailures failures)
        {
            ArgumentNullException.ThrowIfNull(failures);
            bool unfinished;
            lock (gate.sync)
            {
                if (Closed)
                    throw new InvalidOperationException("The helper callback has already ended.");
                Closed = true;
                unfinished = gate.active is not null;
                gate.active?.Reader?.StopAdmission();
            }
            if (unfinished)
                failures.Add(new InvalidOperationException($"Callback for transaction {gate.TransactionId} ended with unfinished execution or an active reader; it cannot commit."),
                    ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
            return DrainAsync(failures);
        }

        private async Task<Lease> DrainAsync(ExecutionFailures failures)
        {
            while (true)
            {
                IHelperTrackedReader? reader;
                Task wait;
                lock (gate.sync)
                {
                    if (gate.active is null)
                    {
                        foreach (var failure in drainedFailures)
                            failures.AddReported(failure, ExecutionFailureStage.CommandExecution);
                        drainedFailures.Clear();
                        return gate.active = new Lease(gate, "complete callback helper", ExecutionOperationKind.TransactionCallback);
                    }
                    reader = gate.active.Reader;
                    gate.active.Reader = null; // Exactly one helper-owned close per reader.
                    wait = (gate.changed ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }
                if (reader is not null)
                {
                    // No lock, request token, deadline or detached provider work here.
                    try { await reader.DrainAsync().ConfigureAwait(false); }
                    catch (Exception failure) { failures.AddReported(failure, ExecutionFailureStage.Cleanup); }
                }
                else
                    await wait.ConfigureAwait(false);
            }
        }

        internal void Observe(Exception failure) => drainedFailures.Add(failure);
    }

    internal Step EnterStep(Lease owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (sync)
        {
            RequireOwner(owner);
            if (owner.ActiveStep is not null)
                throw new InvalidOperationException(
                    $"Transaction {transactionId} already has an active step in '{owner.Operation}'.");
            return owner.ActiveStep = new Step(this, owner);
        }
    }

    private void RequireOwner(Lease owner)
    {
        if (!ReferenceEquals(active, owner))
            throw new InvalidOperationException($"The operation does not own transaction {transactionId}.");
    }

    internal void ValidateStep(Step step)
    {
        ArgumentNullException.ThrowIfNull(step);
        lock (sync)
        {
            if (!ReferenceEquals(active?.ActiveStep, step))
                throw new InvalidOperationException($"The read step does not own transaction {transactionId}.");
        }
    }

    private void RequireIdle(Lease owner)
    {
        RequireOwner(owner);
        if (owner.ActiveStep is not null)
            throw new InvalidOperationException(
                $"Cannot release or transfer transaction {transactionId} while '{owner.Operation}' has an active step.");
    }

    internal sealed class Lease : IDisposable
    {
        private readonly TransactionOperationGate gate;
        private bool released;
        internal string Operation { get; }
        internal ExecutionOperationKind Kind { get; }
        internal string? ProviderInstanceId => gate.ProviderInstanceId;
        internal Step? ActiveStep { get; set; }
        internal IHelperTrackedReader? Reader { get; set; }
        private List<Exception>? failures;
        private bool readerRegistered;

        internal Lease(TransactionOperationGate gate, string operation, ExecutionOperationKind operationKind)
        {
            this.gate = gate;
            Operation = operation;
            Kind = operationKind;
        }

        /// <summary>Hand resource lifetime to a new owner; disposing the old lease is then harmless.</summary>
        internal Lease Transfer()
        {
            lock (gate.sync)
            {
                gate.RequireIdle(this);
                var next = new Lease(gate, Operation, Kind);
                next.Reader = Reader;
                next.failures = failures;
                next.readerRegistered = readerRegistered;
                released = true;
                gate.active = next;
                gate.SignalChange();
                return next;
            }
        }

        internal void RegisterReader(IHelperTrackedReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            lock (gate.sync)
            {
                gate.RequireOwner(this);
                if (readerRegistered)
                    throw new InvalidOperationException("The operation already tracks a reader.");
                readerRegistered = true;
                Reader = reader;
                if (gate.helper?.Closed == true)
                    reader.StopAdmission();
                gate.SignalChange();
            }
        }

        internal void ReportFailure(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            lock (gate.sync)
            {
                gate.RequireOwner(this);
                if (gate.helper is not null)
                    (failures ??= []).Add(failure);
            }
        }

        public void Dispose()
        {
            lock (gate.sync)
            {
                if (released)
                    return;
                gate.RequireIdle(this);
                released = true;
                gate.active = null;
                if (gate.helper?.Closed == true && failures is not null)
                    foreach (var failure in failures)
                        gate.helper.Observe(failure);
                gate.SignalChange();
            }
        }
    }

    internal sealed class Step : IDisposable
    {
        private readonly TransactionOperationGate gate;
        private readonly Lease owner;
        internal ExecutionOperationKind Kind => owner.Kind;
        internal string? ProviderInstanceId => gate.ProviderInstanceId;

        internal Step(TransactionOperationGate gate, Lease owner)
        {
            this.gate = gate;
            this.owner = owner;
        }

        public void Dispose()
        {
            lock (gate.sync)
            {
                // Idempotent disposal must never clear a newer step's ownership.
                if (ReferenceEquals(owner.ActiveStep, this))
                    owner.ActiveStep = null;
            }
        }
    }
}
