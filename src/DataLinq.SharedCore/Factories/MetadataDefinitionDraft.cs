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
        var tableModelValidation = MetadataFactory.ValidateExistingTableModels(metadata);
        if (!tableModelValidation.HasValue)
            return tableModelValidation.Failure;

        var viewDefinitionValidation = MetadataFactory.ValidateViewDefinitions(metadata);
        if (!viewDefinitionValidation.HasValue)
            return viewDefinitionValidation.Failure;

        var primaryKeyValidation = MetadataFactory.ValidateExistingPrimaryKeyColumns(metadata);
        if (!primaryKeyValidation.HasValue)
            return primaryKeyValidation.Failure;

        var indexValidation = MetadataFactory.ValidateExistingColumnIndices(metadata);
        if (!indexValidation.HasValue)
            return indexValidation.Failure;

        var columnPropertyValidation = MetadataFactory.ValidateExistingColumnPropertyBindings(metadata);
        if (!columnPropertyValidation.HasValue)
            return columnPropertyValidation.Failure;

        var defaultValueValidation = MetadataFactory.ValidateValuePropertyDefaults(metadata);
        if (!defaultValueValidation.HasValue)
            return defaultValueValidation.Failure;

        var relationPropertyValidation = MetadataFactory.ValidateExistingRelationPropertyBindings(metadata);
        if (!relationPropertyValidation.HasValue)
            return relationPropertyValidation.Failure;

        var relationValidation = MetadataFactory.ValidateExistingRelationParts(metadata);
        if (!relationValidation.HasValue)
            return relationValidation.Failure;

        return DLOptionFailure.CatchAll(() => MetadataDefinitionSnapshot.Copy(metadata));
    }
}
