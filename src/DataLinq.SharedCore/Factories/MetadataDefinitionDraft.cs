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

        var collectionValidation = MetadataFactory.ValidateMetadataCollections(metadata);
        if (!collectionValidation.HasValue)
            return collectionValidation.Failure;

        var csharpSymbolValidation = MetadataFactory.ValidateCSharpSymbolNames(metadata);
        if (!csharpSymbolValidation.HasValue)
            return csharpSymbolValidation.Failure;

        var tableModelPropertyValidation = MetadataFactory.ValidateUniqueTableModelPropertyNames(metadata);
        if (!tableModelPropertyValidation.HasValue)
            return tableModelPropertyValidation.Failure;

        var objectNameValidation = MetadataFactory.ValidateDatabaseObjectNames(metadata);
        if (!objectNameValidation.HasValue)
            return objectNameValidation.Failure;

        var relationalAttributeValidation = MetadataFactory.ValidateRelationalAttributeMetadata(metadata);
        if (!relationalAttributeValidation.HasValue)
            return relationalAttributeValidation.Failure;

        var cacheMetadataValidation = MetadataFactory.ValidateCacheMetadata(metadata);
        if (!cacheMetadataValidation.HasValue)
            return cacheMetadataValidation.Failure;

        var attributeDatabaseTypeValidation = MetadataFactory.ValidateProviderScopedAttributeDatabaseTypes(metadata);
        if (!attributeDatabaseTypeValidation.HasValue)
            return attributeDatabaseTypeValidation.Failure;

        var schemaAnnotationValidation = MetadataFactory.ValidateSchemaAnnotationMetadata(metadata);
        if (!schemaAnnotationValidation.HasValue)
            return schemaAnnotationValidation.Failure;

        var tableNameValidation = MetadataFactory.ValidateUniqueTableNames(metadata);
        if (!tableNameValidation.HasValue)
            return tableNameValidation.Failure;

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

        var columnTypeValidation = MetadataFactory.ValidateExistingColumnTypes(metadata);
        if (!columnTypeValidation.HasValue)
            return columnTypeValidation.Failure;

        var enumValidation = MetadataFactory.ValidateValuePropertyEnums(metadata);
        if (!enumValidation.HasValue)
            return enumValidation.Failure;

        var columnNameValidation = MetadataFactory.ValidateUniqueColumnNames(metadata);
        if (!columnNameValidation.HasValue)
            return columnNameValidation.Failure;

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
