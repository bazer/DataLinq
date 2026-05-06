using System;

namespace DataLinq.Metadata;

internal static class MetadataMutationGuard
{
    public const string PublicMutationObsoleteMessage = "Runtime metadata definitions are finalized snapshots. Use typed metadata drafts and MetadataDefinitionFactory instead of mutating definitions directly.";

    public static void ThrowIfFrozen(bool isFrozen, object metadata)
    {
        if (isFrozen)
            throw new InvalidOperationException($"{metadata.GetType().Name} is frozen and cannot be mutated after metadata finalization.");
    }
}
