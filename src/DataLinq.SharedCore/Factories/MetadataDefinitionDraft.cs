using System;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;

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

    internal Option<DatabaseDefinition, IDLOptionFailure> TryCreateMutableSnapshot()
    {
        var indexValidation = MetadataFactory.ValidateExistingColumnIndices(metadata);
        if (!indexValidation.HasValue)
            return indexValidation.Failure;

        return DLOptionFailure.CatchAll(() => MetadataDefinitionSnapshot.Copy(metadata));
    }
}
