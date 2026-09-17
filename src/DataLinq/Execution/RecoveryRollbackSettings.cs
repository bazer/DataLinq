using System;

namespace DataLinq.Execution;

/// <summary>Immutable internal capture; public provider/host options remain separate integration work.</summary>
internal sealed class RecoveryRollbackSettings
{
    internal TimeSpan RecoveryRollbackTimeout { get; }

    internal RecoveryRollbackSettings(TimeSpan? recoveryRollbackTimeout = null)
    {
        var value = recoveryRollbackTimeout ?? TimeSpan.FromSeconds(30);
        if (value < TimeSpan.FromMilliseconds(1) || value > TimeSpan.FromMilliseconds(4_294_967_294))
            throw new ArgumentOutOfRangeException(nameof(RecoveryRollbackTimeout), value,
                "Recovery rollback timeout must be between 1 and 4,294,967,294 milliseconds inclusive.");
        RecoveryRollbackTimeout = value;
    }
}
