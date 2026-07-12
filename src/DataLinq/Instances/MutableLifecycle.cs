using System;
using System.Threading;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Instances;

internal enum MutableRowKind
{
    New,
    Existing,
    Deleted
}

internal enum MutableBaselineKind
{
    NoneForNew,
    Committed,
    TransactionLocal,
    Invalid
}

internal enum MutableInvalidationReason
{
    RolledBack,
    OpenTransactionDisposed,
    MutationFailed,
    CommitOutcomeUnknown,
    CommittedStateFinalizationFailed
}

internal enum MutableTransactionOutcome
{
    Unresolved,
    Committed,
    CommittedStateFinalizationFailed
}

internal sealed class MutableTransactionOwnership
{
    private int outcome;

    internal MutableTransactionOwnership(
        IDatabaseProvider provider,
        uint transactionId)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        TransactionId = transactionId;
    }

    internal IDatabaseProvider Provider { get; }
    internal uint TransactionId { get; }
    internal MutableTransactionOutcome Outcome =>
        (MutableTransactionOutcome)Volatile.Read(ref outcome);

    internal void MarkCommittedAfterPublication() =>
        Interlocked.CompareExchange(
            ref outcome,
            (int)MutableTransactionOutcome.Committed,
            (int)MutableTransactionOutcome.Unresolved);

    internal void MarkCommittedStateFinalizationFailed() =>
        Interlocked.CompareExchange(
            ref outcome,
            (int)MutableTransactionOutcome.CommittedStateFinalizationFailed,
            (int)MutableTransactionOutcome.Unresolved);
}

internal readonly record struct MutableBaselineOrigin(
    IDatabaseProvider? ProviderOwner,
    MutableTransactionOwnership? TransactionOwner,
    IDataLinqReadSource? NeutralReadSourceOwner)
{
    internal static MutableBaselineOrigin FromReadSource(IDataLinqReadSource? readSource) =>
        readSource switch
        {
            Transaction transaction => new MutableBaselineOrigin(
                transaction.Provider,
                transaction.MutableOwnership,
                NeutralReadSourceOwner: null),
            IDataSourceAccess dataSource => new MutableBaselineOrigin(
                dataSource.Provider,
                TransactionOwner: null,
                NeutralReadSourceOwner: null),
            null => default,
            _ => new MutableBaselineOrigin(
                ProviderOwner: null,
                TransactionOwner: null,
                NeutralReadSourceOwner: readSource)
        };

    internal static MutableBaselineOrigin FromImmutable(IImmutableInstance immutable)
    {
        ArgumentNullException.ThrowIfNull(immutable);

        if (immutable is IImmutableBaselineOrigin captured)
            return captured.BaselineOrigin;

        try
        {
            return FromReadSource(immutable.GetReadSource());
        }
        catch (NotSupportedException)
        {
            // Custom immutable implementations predating the neutral read-source contract may not
            // expose a source. Preserve construction compatibility and let ML-2 reject unowned SQL
            // writes explicitly instead of guessing an owner here.
            return default;
        }
    }
}

internal readonly record struct MutableLifecycleSnapshot(
    MutableRowKind RowKind,
    MutableBaselineKind BaselineKind,
    IDatabaseProvider? ProviderOwner,
    MutableTransactionOwnership? TransactionOwner,
    IDataLinqReadSource? NeutralReadSourceOwner,
    MutableInvalidationReason? InvalidationReason);

internal interface IImmutableBaselineOrigin
{
    MutableBaselineOrigin BaselineOrigin { get; }
}

internal interface IMutableLifecycle
{
    MutableLifecycleSnapshot Lifecycle { get; }
    DataLinqKey BaselineCanonicalPrimaryKey { get; }

    void AdvanceBaseline(
        IImmutableInstance immutable,
        MutableTransactionOwnership owner);

    void MarkDeleted(MutableTransactionOwnership owner);
    bool TryPromoteCommitted(MutableTransactionOwnership owner);
    void Invalidate(MutableInvalidationReason reason);
}

internal interface IMutableChangeTracking
{
    long MutationVersion { get; }
}

internal sealed class MutableLifecycle
{
    private MutableRowKind rowKind;
    private MutableBaselineKind baselineKind;
    private IDatabaseProvider? providerOwner;
    private MutableTransactionOwnership? transactionOwner;
    private IDataLinqReadSource? neutralReadSourceOwner;
    private MutableInvalidationReason? invalidationReason;
    internal bool HasStoredCommittedBaseline =>
        baselineKind == MutableBaselineKind.Committed &&
        transactionOwner is null;

    private MutableLifecycle(
        MutableRowKind rowKind,
        MutableBaselineKind baselineKind,
        MutableBaselineOrigin origin)
    {
        this.rowKind = rowKind;
        this.baselineKind = baselineKind;
        providerOwner = origin.ProviderOwner;
        transactionOwner = baselineKind == MutableBaselineKind.TransactionLocal
            ? origin.TransactionOwner
            : null;
        neutralReadSourceOwner = origin.NeutralReadSourceOwner;
        invalidationReason = GetOriginInvalidationReason(origin);
    }

    internal static MutableLifecycle New() =>
        new(
            MutableRowKind.New,
            MutableBaselineKind.NoneForNew,
            default);

    internal static MutableLifecycle FromImmutable(IImmutableInstance immutable)
    {
        var origin = MutableBaselineOrigin.FromImmutable(immutable);
        return new MutableLifecycle(
            MutableRowKind.Existing,
            GetBaselineKind(origin),
            origin);
    }

    internal MutableLifecycleSnapshot Snapshot
    {
        get
        {
            var effectiveBaselineKind = baselineKind;
            var effectiveTransactionOwner = transactionOwner;
            var effectiveInvalidationReason = invalidationReason;
            if (effectiveBaselineKind == MutableBaselineKind.TransactionLocal &&
                effectiveTransactionOwner is not null)
            {
                switch (effectiveTransactionOwner.Outcome)
                {
                    case MutableTransactionOutcome.Committed:
                        effectiveBaselineKind = MutableBaselineKind.Committed;
                        effectiveTransactionOwner = null;
                        break;
                    case MutableTransactionOutcome.CommittedStateFinalizationFailed:
                        effectiveBaselineKind = MutableBaselineKind.Invalid;
                        effectiveTransactionOwner = null;
                        effectiveInvalidationReason =
                            MutableInvalidationReason.CommittedStateFinalizationFailed;
                        break;
                }
            }

            return new MutableLifecycleSnapshot(
                rowKind,
                effectiveBaselineKind,
                providerOwner,
                effectiveTransactionOwner,
                neutralReadSourceOwner,
                effectiveInvalidationReason);
        }
    }

    internal bool IsNew
    {
        get
        {
            var snapshot = Snapshot;
            return snapshot.RowKind == MutableRowKind.New ||
                snapshot.BaselineKind == MutableBaselineKind.NoneForNew;
        }
    }

    internal bool IsDeleted => Snapshot.RowKind == MutableRowKind.Deleted;

    internal void ValidateAssignmentReset()
    {
        ThrowIfTerminal(Snapshot, "reset assignments");
    }

    internal void ValidatePublicBaselineReset(MutableBaselineOrigin replacement)
    {
        var snapshot = Snapshot;
        ThrowIfTerminal(snapshot, "reset the baseline");

        if (snapshot.BaselineKind == MutableBaselineKind.NoneForNew)
            return;

        var replacementKind = GetBaselineKind(replacement);
        if (snapshot.BaselineKind == MutableBaselineKind.TransactionLocal)
        {
            if (replacementKind != MutableBaselineKind.TransactionLocal ||
                !ReferenceEquals(snapshot.TransactionOwner, replacement.TransactionOwner))
            {
                throw new InvalidOperationException(
                    "Cannot replace a transaction-local mutable baseline with a baseline from another owner.");
            }

            return;
        }

        if (!HasSameCommittedOwner(snapshot, replacement))
        {
            throw new InvalidOperationException(
                "Cannot replace a mutable baseline with a baseline owned by another data source.");
        }
    }

    internal void ApplyPublicBaselineReset(MutableBaselineOrigin replacement)
    {
        rowKind = MutableRowKind.Existing;
        ApplyOrigin(replacement);
    }

    internal void ValidateHydratedAdvance()
    {
        ThrowIfTerminal(Snapshot, "advance the hydrated baseline");
    }

    internal void AdvanceHydrated(MutableTransactionOwnership owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        rowKind = MutableRowKind.Existing;
        baselineKind = MutableBaselineKind.TransactionLocal;
        providerOwner = owner.Provider;
        transactionOwner = owner;
        neutralReadSourceOwner = null;
        invalidationReason = null;
    }

    internal void MarkDeleted(MutableTransactionOwnership owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var snapshot = Snapshot;

        if (snapshot.BaselineKind == MutableBaselineKind.Invalid)
            ThrowIfTerminal(snapshot, "mark the mutable as deleted");

        rowKind = MutableRowKind.Deleted;
        baselineKind = MutableBaselineKind.TransactionLocal;
        providerOwner = owner.Provider;
        transactionOwner = owner;
        neutralReadSourceOwner = null;
        invalidationReason = null;
    }

    internal bool TryPromoteCommitted(MutableTransactionOwnership owner)
    {
        if (owner is null)
            return false;

        if (owner.Outcome == MutableTransactionOutcome.CommittedStateFinalizationFailed)
            return false;

        if (baselineKind == MutableBaselineKind.Committed)
        {
            return transactionOwner is null &&
                ReferenceEquals(providerOwner, owner.Provider);
        }

        if (baselineKind != MutableBaselineKind.TransactionLocal ||
            !ReferenceEquals(transactionOwner, owner) ||
            !ReferenceEquals(providerOwner, owner.Provider))
        {
            return false;
        }

        baselineKind = MutableBaselineKind.Committed;
        transactionOwner = null;
        neutralReadSourceOwner = null;
        invalidationReason = null;
        return true;
    }

    internal void MarkDeletedWithoutTransaction()
    {
        var snapshot = Snapshot;
        if (snapshot.BaselineKind == MutableBaselineKind.Invalid)
            ThrowIfTerminal(snapshot, "mark the mutable as deleted");

        rowKind = MutableRowKind.Deleted;
    }

    internal void Invalidate(MutableInvalidationReason reason)
    {
        if (baselineKind == MutableBaselineKind.Invalid)
            return;

        baselineKind = MutableBaselineKind.Invalid;
        invalidationReason = reason;
    }

    private static MutableBaselineKind GetBaselineKind(MutableBaselineOrigin origin) =>
        origin.TransactionOwner?.Outcome switch
        {
            null or MutableTransactionOutcome.Committed => MutableBaselineKind.Committed,
            MutableTransactionOutcome.CommittedStateFinalizationFailed => MutableBaselineKind.Invalid,
            _ => MutableBaselineKind.TransactionLocal
        };

    private static MutableInvalidationReason? GetOriginInvalidationReason(
        MutableBaselineOrigin origin) =>
        origin.TransactionOwner?.Outcome ==
            MutableTransactionOutcome.CommittedStateFinalizationFailed
                ? MutableInvalidationReason.CommittedStateFinalizationFailed
                : null;

    private void ApplyOrigin(MutableBaselineOrigin origin)
    {
        baselineKind = GetBaselineKind(origin);
        providerOwner = origin.ProviderOwner;
        transactionOwner = baselineKind == MutableBaselineKind.TransactionLocal
            ? origin.TransactionOwner
            : null;
        neutralReadSourceOwner = origin.NeutralReadSourceOwner;
        invalidationReason = GetOriginInvalidationReason(origin);
    }

    private static bool HasSameCommittedOwner(
        MutableLifecycleSnapshot current,
        MutableBaselineOrigin replacement)
    {
        if (current.ProviderOwner is not null || replacement.ProviderOwner is not null)
            return ReferenceEquals(current.ProviderOwner, replacement.ProviderOwner);

        if (current.NeutralReadSourceOwner is null && replacement.NeutralReadSourceOwner is null)
            return true;

        return ReferenceEquals(
            current.NeutralReadSourceOwner,
            replacement.NeutralReadSourceOwner);
    }

    private static void ThrowIfTerminal(
        MutableLifecycleSnapshot snapshot,
        string operation)
    {
        if (snapshot.BaselineKind == MutableBaselineKind.Invalid)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} because the mutable baseline is invalid ({snapshot.InvalidationReason}).");
        }

        if (snapshot.RowKind == MutableRowKind.Deleted)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} because the mutable row is deleted.");
        }
    }
}
