using DataLinq.Diagnostics;

namespace DataLinq.Mutation;

public partial class Transaction
{
    /// <summary>Gets the latest failure/recovery snapshot for this transaction, or null if no failure has been recorded.</summary>
    /// <remarks>Previously returned snapshots remain unchanged. A rejected competing operation does not overwrite the active operation's state.</remarks>
    public DataLinqFailureContext? FailureContext => DataLinqFailure.Capture(AsyncFailureContext);
}
