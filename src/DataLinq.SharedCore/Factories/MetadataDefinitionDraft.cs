using System;
using DataLinq.Metadata;

namespace DataLinq.Core.Factories;

public sealed class MetadataDefinitionDraft
{
    private readonly DatabaseDefinition metadata;

    private MetadataDefinitionDraft(DatabaseDefinition metadata)
    {
        this.metadata = metadata;
    }

    public static MetadataDefinitionDraft FromMutableMetadata(DatabaseDefinition metadata)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        return new MetadataDefinitionDraft(metadata);
    }

    internal DatabaseDefinition CreateMutableSnapshot() =>
        MetadataDefinitionSnapshot.Copy(metadata);
}
