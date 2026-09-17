using System;

namespace DataLinq.Execution;

/// <summary>
/// Per-transaction, fail-fast admission. An explicit lease, never a thread or ambient
/// execution context, owns the slot. Locks protect transitions only, not provider work.
/// Lifecycle/capability validation and pre-cancellation remain the caller's responsibility.
/// </summary>
internal sealed class TransactionOperationGate(uint transactionId)
{
    private readonly object sync = new();
    private Lease? active;

    internal Lease Enter(string operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        lock (sync)
        {
            ThrowIfBusyCore(operation);
            return active = new Lease(this, operation);
        }
    }

    internal void ThrowIfBusy(string operation)
    {
        lock (sync)
            ThrowIfBusyCore(operation);
    }

    private void ThrowIfBusyCore(string operation)
    {
        if (active is not null)
            throw new InvalidOperationException(
                $"Cannot {operation} through transaction {transactionId} while '{active.Operation}' is active.");
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
        internal Step? ActiveStep { get; set; }

        internal Lease(TransactionOperationGate gate, string operation)
        {
            this.gate = gate;
            Operation = operation;
        }

        /// <summary>Hand resource lifetime to a new owner; disposing the old lease is then harmless.</summary>
        internal Lease Transfer()
        {
            lock (gate.sync)
            {
                gate.RequireIdle(this);
                var next = new Lease(gate, Operation);
                released = true;
                gate.active = next;
                return next;
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
            }
        }
    }

    internal sealed class Step : IDisposable
    {
        private readonly TransactionOperationGate gate;
        private readonly Lease owner;

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
