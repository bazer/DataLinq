using System;
using System.Threading.Tasks;

namespace DataLinq.Memory;

internal static class MemoryAsyncResult
{
    internal static ValueTask<T> FromFailure<T>(Exception failure) =>
        failure is OperationCanceledException ? PreserveCancellation<T>(failure) : ValueTask.FromException<T>(failure);

    // The async method builder preserves the original cancellation exception and
    // canceled status, even for user code that throws with an unrequested token.
    // FromCanceled alone would replace the exception; this completed await never yields.
    private static async ValueTask<T> PreserveCancellation<T>(Exception failure) =>
        await ValueTask.FromException<T>(failure).ConfigureAwait(false);
}
