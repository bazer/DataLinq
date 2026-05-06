using System;

namespace DataLinq.Metadata;

internal static class MetadataMutationGuard
{
    public static void ThrowIfFrozen(bool isFrozen, object metadata)
    {
        if (isFrozen)
            throw new InvalidOperationException($"{metadata.GetType().Name} is frozen and cannot be mutated after metadata finalization.");
    }
}
